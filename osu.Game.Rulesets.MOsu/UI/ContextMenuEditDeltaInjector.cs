// Injects an "Edit delta" item into the song select beatmap right-click menu via reflection
// (same pattern as the toolbar/wave-container and chat injections).
//
// The item flips the current beatmap's realm ruleset to mosu and opens the editor, which
// makes the mosu composer (gimmick preview/toolbox) engage for maps registered as osu.
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
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics.Cursor;
using osu.Game.Graphics.UserInterface;
using osu.Game.Screens;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.MOsu.UI
{
    public static class ContextMenuEditDeltaInjector
    {
        private static readonly FieldInfo? menu_field = typeof(ContextMenuContainer).GetField("menu", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly System.Collections.Generic.HashSet<Menu> hooked_menus = new System.Collections.Generic.HashSet<Menu>();

        private static bool enabled = menu_field != null;

        /// <summary>
        /// Hooks the currently active song select screen's context menu so it gains an "Edit delta"
        /// entry. Safe to call repeatedly (no double-hooking); no-op outside song select.
        /// </summary>
        public static void Hook(OsuGame game, RealmAccess realm)
        {
            if (!enabled)
                return;

            if (game.ScreenStack?.CurrentScreen is not SongSelect songSelect)
                return;

            var beatmap = (IBindable<WorkingBeatmap>)game.Dependencies.Get(typeof(IBindable<WorkingBeatmap>));
            var performer = (IPerformFromScreenRunner)game.Dependencies.Get(typeof(IPerformFromScreenRunner));

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

                    if (menu.Items.Any(i => string.Equals(i.Text.ToString(), @"Edit delta", StringComparison.Ordinal)))
                        return;

                    menu.Items = menu.Items
                                     .Append(new OsuMenuItem(@"Edit delta", MenuItemType.Destructive, () => editDelta(beatmap, realm, performer)))
                                     .ToArray();
                };

                Logger.Log($"[MOsu] Hooked song select context menu for Edit delta");
            }
        }

        private static void editDelta(IBindable<WorkingBeatmap> beatmap, RealmAccess realm, IPerformFromScreenRunner performer)
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

            performer.PerformFromScreen(screen => screen.Push(new EditorLoader()));
        }
    }
}
