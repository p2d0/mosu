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
using osu.Game.Rulesets.MOsu.Beatmaps;
using osu.Game.Rulesets.MOsu.UI;
using osu.Game.Rulesets.MOsu.Gimmicks;
using osu.Game.Rulesets.Osu.Edit;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Screens.Edit;

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

            // Apply gimmicks (including the in-place fake-object replacement) before loadObjects
            // enumerates the playable's HitObjects: mutating the list mid-enumeration throws.
            try
            {
                MosuGimmickRuntime.EnsureApplied(Beatmap, parent.Get<IBindable<WorkingBeatmap>>()?.Value, mutateList: true);
            }
            catch (Exception e)
            {
                Logger.Log($"[MOsu-Editor] Failed to apply gimmicks: {e}");
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
            }

            return base.CreateChildDependencies(parent);
        }

        private bool difficultyMenuHookAttempted;

        private void scheduleDifficultyMenuHook(OsuGame game, RealmAccess realm)
        {
            if (difficultyMenuHookAttempted)
                return;

            difficultyMenuHookAttempted = true;

            ScheduleHookAttempt = () =>
            {
                for (Drawable? d = this; d != null; d = d.Parent)
                {
                    Logger.Log($"[MOsu-Editor] parent walk: {d.GetType().Name}");

                    if (d is Editor editor)
                    {
                        Logger.Log($"[MOsu-Editor] found Editor, hooking menu");
                        CreateMosuDifficultyInjector.Hook(editor, game, realm);
                        return;
                    }
                }

                Logger.Log($"[MOsu-Editor] no Editor in parent chain");
            };
        }

        private System.Action? ScheduleHookAttempt;

        protected override void Update()
        {
            base.Update();

            if (ScheduleHookAttempt != null)
            {
                var attempt = ScheduleHookAttempt;
                ScheduleHookAttempt = null;
                attempt();
            }
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

                MosuGimmickRuntime.EnsureApplied(Beatmap, working, mutateList: true);

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
