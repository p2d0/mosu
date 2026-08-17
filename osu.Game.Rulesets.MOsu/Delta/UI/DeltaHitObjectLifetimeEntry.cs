// Lifetime entry that keeps delta objects on screen through their full preempt window
// (gimmick difficulty overrides change TimePreempt; the stock entry would drop them early).
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.MOsu.Delta.UI
{
    public class DeltaHitObjectLifetimeEntry : HitObjectLifetimeEntry
    {
        public DeltaHitObjectLifetimeEntry(HitObject hitObject)
            : base(hitObject)
        {
            LifetimeEnd = HitObject.GetEndTime() + HitObject.HitWindows.WindowFor(HitResult.Miss);
        }

        protected override double InitialLifetimeOffset => ((OsuHitObject)HitObject).TimePreempt;
    }
}
