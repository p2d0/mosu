// Fake-note punish-misses should count against accuracy like a real miss.
//
// The delta fork implements this by refactoring the core ScoreProcessor to route the
// accuracy denominator through virtual helpers. The NuGet ApplyResultInternal is sealed,
// so MOsu replicates the delta behaviour by post-adjusting the (private) accuracy
// denominator fields via reflection after each judgement.
//
// The reflection is defensive: if the private fields are renamed or change type in a
// future osu version, the adjustment silently disables itself instead of crashing.

using System;
using System.Linq;
using System.Reflection;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.MOsu.Delta.Gimmicks;
using osu.Game.Rulesets.MOsu.Delta.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.MOsu.Delta.Scoring
{
    public partial class DeltaScoreProcessor : OsuScoreProcessor
    {
        private static readonly FieldInfo? max_base_score_field = lookupField("currentMaximumBaseScore", typeof(double));
        private static readonly FieldInfo? accuracy_judgement_count_field = lookupField("currentAccuracyJudgementCount", typeof(int));

        private static bool adjustmentEnabled = max_base_score_field != null && accuracy_judgement_count_field != null;

        private IBeatmap? appliedBeatmap;
        private bool needsResim;

        public DeltaScoreProcessor()
        {
            NewJudgement += onNewJudgement;
            JudgementReverted += onJudgementReverted;
            DeltaGimmickRuntime.GimmicksApplied += onGimmicksApplied;
        }

        public override void ApplyBeatmap(IBeatmap beatmap)
        {
            appliedBeatmap = beatmap;

            // If the gimmicks aren't parsed yet, the fake sources are still normal objects in
            // the list, so this simulation will count them. The drawable ruleset's constructor
            // normally applies from the cache first; when that missed, re-simulate after parse.
            needsResim = beatmap is Beatmaps.DeltaBeatmap mosu
                         && !mosu.Gimmicks.Parsed
                         && !beatmap.HitObjects.OfType<FakeHitCircle>().Any()
                         && !beatmap.HitObjects.OfType<FakeSlider>().Any();

            base.ApplyBeatmap(beatmap);
        }

        private void onGimmicksApplied()
        {
            if (!needsResim || appliedBeatmap == null)
                return;

            try
            {
                // Replicate ApplyBeatmap's tail: zero, simulate against the now-mutated playable
                // (fakes judge as IgnoreHit, no combo), snapshot the maximum stats, zero again.
                // This runs before gameplay starts, so there is no live state to disturb.
                Reset(false);
                SimulateAutoplay(appliedBeatmap);
                Reset(true);
                Reset(false);

                Logger.Log($"[MOsu-Score] resimulated max stats: {string.Join(',', MaximumResultCounts.Select(k => $"{k.Key}={k.Value}"))}");
                needsResim = false;
            }
            catch (Exception e)
            {
                Logger.Error(e, "resimulate max stats failed");
            }
        }

        private void onNewJudgement(JudgementResult result)
        {
            if (!isFakePunishMiss(result))
                return;

            // Treat fake miss punishment as a regular osu! miss for the accuracy denominator.
            adjustAccuracyDenominator(GetBaseScoreForResult(HitResult.Great));
        }

        private void onJudgementReverted(JudgementResult result)
        {
            if (!isFakePunishMiss(result))
                return;

            adjustAccuracyDenominator(-GetBaseScoreForResult(HitResult.Great));
        }

        private void adjustAccuracyDenominator(int delta)
        {
            if (!adjustmentEnabled)
                return;

            try
            {
                max_base_score_field!.SetValue(this, (double)max_base_score_field.GetValue(this)! + delta);
                accuracy_judgement_count_field!.SetValue(this, (int)accuracy_judgement_count_field.GetValue(this)! + Math.Sign(delta));
            }
            catch
            {
                // Unexpected reflection failure (e.g. field shape changed in a future osu version).
                // Disable the adjustment permanently rather than throwing on every fake miss.
                adjustmentEnabled = false;
            }
        }

        private static bool isFakePunishMiss(JudgementResult result)
            => result.Type == HitResult.Miss
               && result.Judgement.MaxResult == HitResult.IgnoreHit
               && result.HitObject is FakeHitCircle or FakeSlider;

        private static FieldInfo? lookupField(string name, Type type)
        {
            var field = typeof(ScoreProcessor).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return field != null && field.FieldType == type ? field : null;
        }
    }
}
