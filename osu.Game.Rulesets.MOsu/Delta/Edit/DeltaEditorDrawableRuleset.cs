// Editor-safe drawable ruleset: applies gimmicks to the compose playfield without
// any gameplay-only dependencies (Player, GameplayClockContainer, ...).

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.MOsu.Delta.Beatmaps;
using osu.Game.Rulesets.MOsu.UI;
using osu.Game.Rulesets.MOsu.Delta.Gimmicks;
using osu.Game.Rulesets.MOsu.Delta.UI;
using osu.Game.Rulesets.Osu.Edit;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Screens.Edit;

namespace osu.Game.Rulesets.MOsu.Delta.Edit
{
    public partial class DeltaEditorDrawableRuleset : DrawableOsuEditorRuleset
    {
        private bool gimmicksApplied;

        private IReadOnlyDependencyContainer parentDependencies = null!;

        public DeltaEditorDrawableRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod> mods)
            : base(ruleset, beatmap, mods)
        {
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            // [Resolved] fields come back null in the editor context, so capture the parent chain
            // and resolve dependencies manually (same pattern ComposeScreen uses).
            parentDependencies = parent;

            // Apply gimmicks (including the in-place fake-object replacement) before loadObjects
            // enumerates the playable's HitObjects: mutating the list mid-enumeration throws.
            try
            {
                DeltaGimmickRuntime.EnsureApplied(Beatmap, parent.Get<IBindable<WorkingBeatmap>>()?.Value, mutateList: true);
            }
            catch (Exception e)
            {
                Logger.Log($"[MOsu-Editor] Failed to apply gimmicks: {e}");
                Logger.Log("[MOsu-Editor] osu! api changed: gimmicks will be disabled. Please update mosu! or report the issue on GitHub.", level: LogLevel.Important);
            }

            // Hook File -> Create New Difficulty -> MOsu! once attached (Parent is null during
            // CreateChildDependencies, so defer to the first Update).
            try
            {
                var game = parent.Get<OsuGame>();
                var realm = parent.Get<RealmAccess>();
                scheduleDifficultyMenuHook(game, realm);
            }
            catch (Exception e)
            {
                Logger.Log($"[MOsu-Editor] Failed to hook Create New Difficulty: {e}");
                Logger.Log("[MOsu-Editor] osu! api changed: Create New Difficulty injection will be disabled. Please update mosu! or report the issue on GitHub.", level: LogLevel.Important);
            }

            return base.CreateChildDependencies(parent);
        }

        private void scheduleDifficultyMenuHook(OsuGame game, RealmAccess realm)
        {
            // Parent is null during CreateChildDependencies, so defer until the drawable is
            // attached (Schedule runs at the start of the first UpdateSubTree).
            Schedule(() =>
            {
                for (Drawable? d = this; d != null; d = d.Parent)
                {
                    if (d is Editor editor)
                    {
                        CreateMosuDifficultyInjector.Hook(editor, game, realm);
                        return;
                    }
                }

                Logger.Log($"[MOsu-Editor] no Editor in parent chain");
            });
        }

        public override DrawableHitObject<OsuHitObject>? CreateDrawableRepresentation(OsuHitObject h)
        {
            if (!gimmicksApplied)
            {
                gimmicksApplied = true;

                WorkingBeatmap? working = null;

                try
                {
                    working = parentDependencies.Get<IBindable<WorkingBeatmap>>()?.Value;
                }
                catch
                {
                }

                DeltaGimmickRuntime.EnsureApplied(Beatmap, working, mutateList: true);

            }

            var drawable = DeltaGimmickRuntime.CreateGimmickDrawableRepresentation(Beatmap, h);

            return drawable;
        }

        /// <summary>
        /// Re-creates the compose playfield drawables so fake/hidden type changes and
        /// difficulty overrides become visible immediately after a model mutation.
        /// </summary>
        public void RefreshDrawables()
        {
            foreach (var h in Beatmap.HitObjects.ToList())
            {
                RemoveHitObject(h);
                AddHitObject(h);
            }
        }
    }
}
