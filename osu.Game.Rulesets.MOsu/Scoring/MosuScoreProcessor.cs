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
using System.Collections.Generic;
using System.Reflection;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.MOsu.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.MOsu.Scoring
{
    public partial class MosuScoreProcessor : OsuScoreProcessor
    {
        private static readonly FieldInfo? max_base_score_field = lookupField("currentMaximumBaseScore", typeof(double));
        private static readonly FieldInfo? accuracy_judgement_count_field = lookupField("currentAccuracyJudgementCount", typeof(int));

        private static bool adjustmentEnabled = max_base_score_field != null && accuracy_judgement_count_field != null;

        public MosuScoreProcessor()
        {
            NewJudgement += onNewJudgement;
            JudgementReverted += onJudgementReverted;
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
