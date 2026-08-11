// Editor-safe drawable ruleset: applies gimmicks to the compose playfield without
// any gameplay-only dependencies (Player, GameplayClockContainer, ...).

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
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            // [Resolved] fields come back null in the editor context, so capture the parent chain
            // and resolve dependencies manually (same pattern ComposeScreen uses).
            parentDependencies = parent;
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

                Logger.Log($"[MOsu-Editor] playable={Beatmap.GetType().Name} working={working?.GetType().Name} path={working?.BeatmapInfo.Path}");

                MosuGimmickRuntime.EnsureApplied(Beatmap, working);

                if (Beatmap is MosuBeatmap mosuBeatmap2)
                {
                    Logger.Log($"[MOsu-Editor] after apply: entries={mosuBeatmap2.Gimmicks.HitObjectGimmicks.Entries.Count} applied={mosuBeatmap2.Gimmicks.Applied} firstObject={Beatmap.HitObjects.FirstOrDefault()?.GetType().Name}");
                }
            }

            var drawable = MosuGimmickRuntime.CreateGimmickDrawableRepresentation(Beatmap, h);

            if (drawable != null && !reportedGimmickDrawable)
            {
                reportedGimmickDrawable = true;
                Logger.Log($"[MOsu-Editor] first gimmick drawable: {drawable.GetType().Name}");
            }

            return drawable;
        }

        private bool reportedGimmickDrawable;
    }
}
