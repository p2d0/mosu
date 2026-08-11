// Injects a "Make map delta" item into the song select beatmap right-click menu via reflection
// (same pattern as the toolbar/wave-container and chat injections).
//
// The item flips the current beatmap's realm ruleset to mosu, so the map is treated as a mosu
// beatmap when played: the mosu converter wraps it and the gameplay runtime parses + applies its
// delta gimmick sections. It does not open the editor.
//
// Only the song select screen's own context menu container is hooked (it persists for the
// lifetime of the screen), never context menus elsewhere in the game.

using System;
using System.Linq;
using System.Reflection;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Logging;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics.Cursor;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays;
using osu.Game.Overlays.Dialog;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.MOsu.UI
{
    public static class ContextMenuMakeDeltaInjector
    {
        private static readonly FieldInfo? menu_field = typeof(ContextMenuContainer).GetField("menu", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly System.Collections.Generic.HashSet<Menu> hooked_menus = new System.Collections.Generic.HashSet<Menu>();

        private static bool enabled = menu_field != null;

        /// <summary>
        /// Hooks the currently active song select screen's context menu so it gains a "Make map delta"
        /// entry. Safe to call repeatedly (no double-hooking); no-op outside song select.
        /// </summary>
        public static void Hook(OsuGame game, RealmAccess realm)
        {
            if (!enabled)
                return;

            if (game.ScreenStack?.CurrentScreen is not SongSelect songSelect)
                return;

            var beatmap = (IBindable<WorkingBeatmap>)game.Dependencies.Get(typeof(IBindable<WorkingBeatmap>));

            foreach (var container in songSelect.ChildrenOfType<OsuContextMenuContainer>())
            {
                var menu = menu_field?.GetValue(container) as Menu;
                if (menu == null)
                    continue;

                if (!hooked_menus.Add(menu))
                    continue;

                menu.StateChanged += state =>
                {
                    if (state != MenuState.Open)
                        return;

                    if (menu.Items.Any(i => string.Equals(i.Text.ToString(), @"Make map delta", StringComparison.Ordinal)))
                        return;

                    var existing = menu.Items.ToList();
                    existing.Insert(1, new OsuMenuItem(@"Make map delta", MenuItemType.Destructive, () => makeMapDelta(game, beatmap, realm)));
                    menu.Items = existing.ToArray();
                };

                Logger.Log($"[MOsu] Hooked song select context menu for Make map delta");
            }
        }

        private static void makeMapDelta(OsuGame game, IBindable<WorkingBeatmap> beatmap, RealmAccess realm)
        {
            var info = beatmap.Value?.BeatmapInfo;
            if (info == null)
                return;

            IDialogOverlay? dialogOverlay = null;
            try
            {
                dialogOverlay = game.Dependencies.Get(typeof(IDialogOverlay)) as IDialogOverlay;
            }
            catch
            {
            }

            if (dialogOverlay == null)
                flipToMosu(beatmap, realm);
            else
                dialogOverlay.Push(new ConfirmDialog(
                    "Make this map a mosu (delta) map? Will allow editing delta gimmicks but the map wont be available in regular osu! (only mosu!)",
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
