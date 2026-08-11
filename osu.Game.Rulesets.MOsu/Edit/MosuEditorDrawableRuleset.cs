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
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.MOsu.Beatmaps;
using osu.Game.Rulesets.MOsu.Gimmicks;
using osu.Game.Rulesets.Osu.Edit;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Objects.Drawables;

namespace osu.Game.Rulesets.MOsu.Edit
{
    public partial class MosuEditorDrawableRuleset : DrawableOsuEditorRuleset
    {
        private bool gimmicksApplied;

        private IReadOnlyDependencyContainer parentDependencies = null!;

        public MosuEditorDrawableRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod> mods)
            : base(ruleset, beatmap, mods)
        {
            // Apply gimmicks from the cache before loadObjects enumerates the playable, so the
            // in-place fake replacements never happen mid-enumeration.
            try
            {
                MosuGimmickRuntime.EnsureAppliedFromCache(beatmap);
            }
            catch (Exception e)
            {
                Logger.Log($"[MOsu-Editor] cache-apply failed: {e}");
            }
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
                MosuGimmickRuntime.EnsureApplied(Beatmap, parent.Get<IBindable<WorkingBeatmap>>()?.Value);
            }
            catch (Exception e)
            {
                Logger.Log($"[MOsu-Editor] Failed to apply gimmicks: {e}");
            }

            return base.CreateChildDependencies(parent);
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

                MosuGimmickRuntime.EnsureApplied(Beatmap, working);

            }

            var drawable = MosuGimmickRuntime.CreateGimmickDrawableRepresentation(Beatmap, h);

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
