// Makes File -> Create New Difficulty -> MOsu! actually work.
//
// The core's CreateNewDifficulty dies for non-legacy rulesets: addDifficultyToSet ->
// save() encodes with LegacyBeatmapEncoder, which rejects the mosu ruleset. This hooks
// the editor's File menu (same reflection/walk pattern as the song-select hook), swaps
// the mosu item's action, and performs the creation itself: build a fresh empty difficulty
// with the mosu ruleset + cloned timing points, write it via the osu-mode encoder, store it
// in the realm set, then switch the editor to it.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Screens;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Logging;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Database;
using osu.Game.Rulesets.MOsu.Gimmicks;
using osu.Game.Rulesets.MOsu.Beatmaps;
using osu.Game.Overlays;
using osu.Framework.Platform;
using osu.Game.Extensions;
using osu.Game.Models;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Utils;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Edit.Components.Menus;

namespace osu.Game.Rulesets.MOsu.UI
{
    public static class CreateMosuDifficultyInjector
    {
        private static readonly string mosu_ruleset_name = new MosuRuleset().Description;

        /// <summary>
        /// Hooks the given editor's File menu so the MOsu! "Create New Difficulty" item
        /// uses the mosu creation path. Safe to call repeatedly; no-op once hooked.
        /// </summary>
        public static void Hook(Editor editor, OsuGame game, RealmAccess realm)
        {
            var menuBars = editor.ChildrenOfType<EditorMenuBar>().ToList();
            Logger.Log($"[MOsu-CreateDifficulty] Hook: {menuBars.Count} menu bars");

            foreach (var menuBar in menuBars)
            {
                foreach (var topLevel in menuBar.Items)
                {
                    foreach (var fileItem in topLevel.Items ?? Enumerable.Empty<MenuItem>())
                    {
                        foreach (var rulesetItem in fileItem.Items ?? Enumerable.Empty<MenuItem>())
                        {
                            Logger.Log($"[MOsu-CreateDifficulty] ruleset item '{rulesetItem.Text}' vs '{mosu_ruleset_name}' match={string.Equals(rulesetItem.Text.ToString(), mosu_ruleset_name, StringComparison.Ordinal)}");

                            if (!string.Equals(rulesetItem.Text.ToString(), mosu_ruleset_name, StringComparison.Ordinal))
                                continue;

                            rulesetItem.Action.Value = () =>
                            {
                                IDialogOverlay? dialogOverlay = null;

                                try
                                {
                                    dialogOverlay = game.Dependencies.Get(typeof(IDialogOverlay)) as IDialogOverlay;
                                }
                                catch
                                {
                                }

                                if (dialogOverlay != null)
                                    dialogOverlay.Push(new CreateNewDifficultyDialog(createCopy => createMosuDifficulty(game, realm, createCopy)));
                                else
                                    createMosuDifficulty(game, realm, false);
                            };
                            Logger.Log($"[MOsu] Hooked editor File -> Create New Difficulty -> {mosu_ruleset_name}");
                            return;
                        }
                    }
                }
            }
        }

        private static void createMosuDifficulty(OsuGame game, RealmAccess realm, bool createCopy)
        {
            var beatmapBindable = (IBindable<WorkingBeatmap>)game.Dependencies.Get(typeof(IBindable<WorkingBeatmap>));
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

                if (newBeatmap is MosuBeatmap mosu)
                    writer.Write(MosuGimmickSerializer.Serialize(mosu.Gimmicks.Sections, mosu.Gimmicks.HitObjectGimmicks));

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
            MosuGimmickRuntime.EnsureApplied(playable, reference, mutateList: false);

            var copy = new MosuBeatmap
            {
                BeatmapInfo = newInfo,
                Bookmarks = reference.Beatmap.Bookmarks.ToArray(),
                Difficulty = reference.Beatmap.Difficulty,
                ControlPointInfo = playable.ControlPointInfo,
                Gimmicks = playable is MosuBeatmap source ? source.Gimmicks : new MosuGimmickData(),
            };

            foreach (var h in playable.HitObjects.OfType<OsuHitObject>())
                copy.HitObjects.Add(h);

            return copy;
        }
    }
}
