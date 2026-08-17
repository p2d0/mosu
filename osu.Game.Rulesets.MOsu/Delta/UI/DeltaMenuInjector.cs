// Single adapter owning all osu! menu injection for delta features. Consolidates the editor
// File-menu hook (Save / Create New Difficulty -> MOsu!) and the song select context-menu hook
// ("Make map delta") into one module with one failure path: when osu!'s menus change shape,
// the feature logs once (Important) and disables itself instead of crashing.
//
// All string-matching against osu! UI labels lives here, so a label rename in core is one
// edit instead of two.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Framework.Screens;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Graphics.Cursor;
using osu.Game.Graphics.UserInterface;
using osu.Game.Models;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;
using osu.Game.Rulesets;
using osu.Game.Rulesets.MOsu.Delta.Beatmaps;
using osu.Game.Rulesets.MOsu.Delta.Edit;
using osu.Game.Rulesets.MOsu.Delta.Gimmicks;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Edit.Components.Menus;
using osu.Game.Screens.Select;
using osu.Game.Utils;

namespace osu.Game.Rulesets.MOsu.Delta.UI
{
    public static class DeltaMenuInjector
    {
        private static readonly string mosu_ruleset_name = new MosuRuleset().Description;

        private const string save_label = "Save";
        private const string make_map_delta_label = "Make map delta";

        private static readonly HashSet<string> disabled_features_logged = new HashSet<string>();

        private static void logFeatureDisabled(string feature, Exception e)
        {
            Logger.Log($"[MOsu] {feature} failed: {e}");

            if (disabled_features_logged.Add(feature))
                Logger.Log($"[MOsu] osu! api changed: {feature} will be disabled. Please update mosu! or report the issue on GitHub.", level: LogLevel.Important);
        }

        private static IDialogOverlay? resolveDialogOverlay(OsuGame game)
        {
            try
            {
                return game.Dependencies.Get(typeof(IDialogOverlay)) as IDialogOverlay;
            }
            catch
            {
                return null;
            }
        }

        private static IBindable<WorkingBeatmap> resolveWorkingBeatmap(OsuGame game)
            => (IBindable<WorkingBeatmap>)game.Dependencies.Get(typeof(IBindable<WorkingBeatmap>));

        // ---- editor File menu ----

        /// <summary>
        /// Hooks the given editor's File menu so "Save" uses the mosu saver (the core save
        /// rejects non-legacy rulesets) and the MOsu! "Create New Difficulty" item uses the
        /// mosu creation path. Safe to call repeatedly; no-op once hooked.
        /// </summary>
        public static void HookEditorFileMenu(Editor editor, OsuGame game, RealmAccess realm)
        {
            try
            {
                var menuBars = editor.ChildrenOfType<EditorMenuBar>().ToList();
                var saveComposer = editor.ChildrenOfType<DeltaHitObjectComposer>().FirstOrDefault();

                foreach (var menuBar in menuBars)
                {
                    foreach (var topLevel in menuBar.Items)
                    {
                        foreach (var fileItem in topLevel.Items ?? Enumerable.Empty<MenuItem>())
                        {
                            // File -> Save should use the mosu saver (the core save rejects non-legacy rulesets).
                            if (string.Equals(fileItem.Text.ToString(), save_label, StringComparison.Ordinal)
                                && saveComposer is DeltaHitObjectComposer composer)
                            {
                                fileItem.Action.Value = () => composer.save();
                                Logger.Log("[MOsu] Hooked editor File -> Save to mosu saver");
                            }

                            foreach (var rulesetItem in fileItem.Items ?? Enumerable.Empty<MenuItem>())
                            {
                                if (!string.Equals(rulesetItem.Text.ToString(), mosu_ruleset_name, StringComparison.Ordinal))
                                    continue;

                                rulesetItem.Action.Value = () =>
                                {
                                    var dialogOverlay = resolveDialogOverlay(game);

                                    if (dialogOverlay != null)
                                        dialogOverlay.Push(new CreateNewDifficultyDialog(createCopy => createDeltaDifficulty(game, realm, createCopy)));
                                    else
                                        createDeltaDifficulty(game, realm, false);
                                };
                                Logger.Log($"[MOsu] Hooked editor File -> Create New Difficulty -> {mosu_ruleset_name}");
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                logFeatureDisabled("editor File menu injection", e);
            }
        }

        // The core's CreateNewDifficulty dies for non-legacy rulesets: addDifficultyToSet ->
        // save() encodes with LegacyBeatmapEncoder, which rejects the mosu ruleset. This swaps
        // the mosu item's action and performs the creation itself: build a fresh empty difficulty
        // with the mosu ruleset + cloned timing points, write it via the osu-mode encoder, store
        // it in the realm set, then switch the editor to it.
        private static void createDeltaDifficulty(OsuGame game, RealmAccess realm, bool createCopy)
        {
            var beatmapBindable = resolveWorkingBeatmap(game);
            var referenceWorking = beatmapBindable.Value;
            var referenceInfo = referenceWorking?.BeatmapInfo;
            var set = referenceInfo?.BeatmapSet;

            if (referenceWorking == null || referenceInfo == null || set == null)
                return;

            var rulesetInfo = realm.Run(r => r.Find<RulesetInfo>(new MosuRuleset().ShortName));
            if (rulesetInfo == null)
                return;

            var newInfo = new BeatmapInfo(rulesetInfo, new BeatmapDifficulty(), referenceInfo.Metadata.DeepClone())
            {
                DifficultyName = NamingUtils.GetNextBestName(set.Beatmaps.Select(b => b.DifficultyName), "New Difficulty")
            };

            IBeatmap newBeatmap = createCopy
                ? copyCurrentDifficulty(referenceWorking, newInfo)
                : createEmptyDifficulty(referenceWorking, newInfo);

            string text;

            try
            {
                var originalRuleset = newInfo.Ruleset;
                newInfo.Ruleset = new RulesetInfo { OnlineID = 0, ShortName = "osu" };

                using var writer = new StringWriter();
                new LegacyBeatmapEncoder(newBeatmap, null, null).Encode(writer);

                if (newBeatmap is DeltaBeatmap mosu)
                    writer.Write(DeltaGimmickSerializer.Serialize(mosu.Gimmicks.Sections, mosu.Gimmicks.HitObjectGimmicks));

                text = writer.ToString();
                newInfo.Ruleset = originalRuleset;
            }
            catch (Exception e)
            {
                Logger.Error(e, "[MOsu] Create New Difficulty encode failed");
                return;
            }

            byte[] data = Encoding.UTF8.GetBytes(text);
            string filename = $"{newInfo.Metadata.Artist} - {newInfo.Metadata.Title} ({newInfo.Metadata.Author.Username}) [{newInfo.DifficultyName}].osu".GetValidFilename();
            var storage = game.Dependencies.Get(typeof(Storage)) as Storage;

            try
            {
                realm.Write(r =>
                {
                    var liveSet = r.Find<BeatmapSetInfo>(set.ID);
                    if (liveSet == null)
                        return;

                    var fileStore = new RealmFileStore(realm, storage);
                    var realmFile = fileStore.Add(new MemoryStream(data), r);
                    liveSet.Files.Add(new RealmNamedFileUsage(realmFile, filename));

                    newInfo.MD5Hash = new MemoryStream(data).ComputeMD5Hash();
                    newInfo.Hash = new MemoryStream(data).ComputeSHA2Hash();
                    newInfo.LastLocalUpdate = DateTimeOffset.Now;
                    newInfo.Status = BeatmapOnlineStatus.LocallyModified;
                    newInfo.BeatmapSet = liveSet;

                    liveSet.Beatmaps.Add(newInfo);
                    newInfo.UpdateStatisticsFromBeatmap(newBeatmap);
                });
            }
            catch (Exception e)
            {
                Logger.Error(e, "[MOsu] Create New Difficulty save failed");
                return;
            }

            Logger.Log($"[MOsu] created new mosu difficulty {newInfo.DifficultyName}");

            // Switch the editor to the new difficulty via the loader's existing-difficulty path
            // (ScheduleSwitchToNewDifficulty would re-run the core's CreateNewDifficulty, which
            // dies on the legacy encoder for non-legacy rulesets).
            if (game.ScreenStack?.CurrentScreen is Editor editor
                && editor.GetParentScreen() is EditorLoader loader)
            {
                try
                {
                    loader.ScheduleSwitchToExistingDifficulty(newInfo, null);
                    return;
                }
                catch (Exception e)
                {
                    Logger.Log($"[MOsu] Create New Difficulty switch failed, falling back: {e}");
                }
            }

            // Fallback: point the game at the new difficulty and let the editor reload.
            var cache = game.Dependencies.Get(typeof(IWorkingBeatmapCache)) as IWorkingBeatmapCache;
            var working = cache?.GetWorkingBeatmap(newInfo);

            if (working == null)
                return;

            if (game.Dependencies.Get(typeof(IBindable<RulesetInfo>)) is IBindable<RulesetInfo> rulesetBindable)
                ((Bindable<RulesetInfo>)rulesetBindable).Value = rulesetInfo;

            ((Bindable<WorkingBeatmap>)beatmapBindable).Value = working;
        }

        private static IBeatmap createEmptyDifficulty(WorkingBeatmap reference, BeatmapInfo newInfo)
        {
            var beatmap = new Beatmap
            {
                BeatmapInfo = newInfo,
                Bookmarks = reference.Beatmap.Bookmarks.ToArray(),
            };

            foreach (var timingPoint in reference.Beatmap.ControlPointInfo.TimingPoints)
                beatmap.ControlPointInfo.Add(timingPoint.Time, timingPoint.DeepClone());

            foreach (var effectPoint in reference.Beatmap.ControlPointInfo.EffectPoints)
                beatmap.ControlPointInfo.Add(effectPoint.Time, effectPoint.DeepClone());

            return beatmap;
        }

        private static IBeatmap copyCurrentDifficulty(WorkingBeatmap reference, BeatmapInfo newInfo)
        {
            var playable = reference.GetPlayableBeatmap(new MosuRuleset().RulesetInfo);
            DeltaGimmickRuntime.EnsureApplied(playable, reference, mutateList: false);

            var copy = new DeltaBeatmap
            {
                BeatmapInfo = newInfo,
                Bookmarks = reference.Beatmap.Bookmarks.ToArray(),
                Difficulty = reference.Beatmap.Difficulty,
                ControlPointInfo = playable.ControlPointInfo,
                Gimmicks = playable is DeltaBeatmap source ? source.Gimmicks : new DeltaGimmickData(),
            };

            foreach (var h in playable.HitObjects.OfType<OsuHitObject>())
                copy.HitObjects.Add(h);

            return copy;
        }

        // ---- song select context menu ----

        // Injects a "Make map delta" item into the song select beatmap right-click menu. The item
        // flips the current beatmap's realm ruleset to mosu, so the map is treated as a mosu
        // beatmap when played. Only the song select screen's own context menu container is hooked
        // (it persists for the lifetime of the screen), never context menus elsewhere in the game.
        private static readonly FieldInfo? menu_field = typeof(ContextMenuContainer).GetField("menu", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly HashSet<Menu> hooked_menus = new HashSet<Menu>();

        /// <summary>
        /// Hooks the currently active song select screen's context menu so it gains a "Make map delta"
        /// entry. Safe to call repeatedly (no double-hooking); no-op outside song select.
        /// </summary>
        public static void HookSongSelectContextMenu(OsuGame game, RealmAccess realm)
        {
            if (menu_field == null)
            {
                logFeatureDisabled("song select context menu injection", new InvalidOperationException("ContextMenuContainer.menu field not found"));
                return;
            }

            try
            {
                if (game.ScreenStack?.CurrentScreen is not SongSelect songSelect)
                    return;

                var beatmap = resolveWorkingBeatmap(game);

                foreach (var container in songSelect.ChildrenOfType<OsuContextMenuContainer>())
                {
                    var menu = menu_field.GetValue(container) as Menu;
                    if (menu == null)
                        continue;

                    if (!hooked_menus.Add(menu))
                        continue;

                    menu.StateChanged += state =>
                    {
                        if (state != MenuState.Open)
                            return;

                        if (menu.Items.Any(i => string.Equals(i.Text.ToString(), make_map_delta_label, StringComparison.Ordinal)))
                            return;

                        var existing = menu.Items.ToList();
                        existing.Insert(1, new OsuMenuItem(make_map_delta_label, MenuItemType.Destructive, () => makeMapDelta(game, beatmap, realm)));
                        menu.Items = existing.ToArray();
                    };

                    Logger.Log("[MOsu] Hooked song select context menu for Make map delta");
                }
            }
            catch (Exception e)
            {
                logFeatureDisabled("song select context menu injection", e);
            }
        }

        private static void makeMapDelta(OsuGame game, IBindable<WorkingBeatmap> beatmap, RealmAccess realm)
        {
            var info = beatmap.Value?.BeatmapInfo;
            if (info == null)
                return;

            var dialogOverlay = resolveDialogOverlay(game);

            if (dialogOverlay == null)
                flipToMosu(beatmap, realm);
            else
                dialogOverlay.Push(new ConfirmDialog(
                    "Make this map a mosu (delta) map? Will allow editing delta gimmicks but the map will not be available in regular osu! (only mosu!)",
                    () => flipToMosu(beatmap, realm)));
        }

        private static void flipToMosu(IBindable<WorkingBeatmap> beatmap, RealmAccess realm)
        {
            var info = beatmap.Value?.BeatmapInfo;
            if (info == null)
                return;

            realm.Write(r =>
            {
                var managed = r.Find<BeatmapInfo>(info.ID);
                var mosuRuleset = r.Find<RulesetInfo>(new MosuRuleset().ShortName);

                if (managed == null || mosuRuleset == null)
                    return;

                if (managed.Ruleset.ShortName != mosuRuleset.ShortName)
                    managed.Ruleset = mosuRuleset;
            });
        }
    }
}
