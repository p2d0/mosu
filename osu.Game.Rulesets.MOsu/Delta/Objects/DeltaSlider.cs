// Slider with delta's removed 10x slider-velocity cap.
//
// The stock core clamps slider velocity in three places (the per-object bindable here, the
// DifficultyControlPoint bindable, and the static GetPrecisionAdjustedBeatLength formula),
// so maps with SV > 10x decode and play at 10x. Delta removes all three in core; this
// subclass replicates the net effect from the ruleset side: uncap the bindables and
// recompute the path velocity/tick distance with the uncapped formula.
//
// Ported from delta fork changes to osu.Game.Rulesets.Osu/Objects/Slider.cs,
// osu.Game/Beatmaps/ControlPoints/DifficultyControlPoint.cs and
// osu.Game/Rulesets/Objects/Legacy/LegacyRulesetExtensions.cs.
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Reflection;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.MOsu.Delta.Objects
{
    public class DeltaSlider : Slider
    {
        public DeltaSlider()
        {
            // Delta: MinValue 0.1 -> 0, MaxValue 10 -> 1000 on the base bindable.
            SliderVelocityMultiplierBindable.MinValue = 0;
            SliderVelocityMultiplierBindable.MaxValue = 1000;
        }

        protected override void ApplyDefaultsToSelf(ControlPointInfo controlPointInfo, IBeatmapDifficultyInfo difficulty)
        {
            base.ApplyDefaultsToSelf(controlPointInfo, difficulty);

            // The stock formula (GetPrecisionAdjustedBeatLength) clamps the effective SV to 10x
            // via Math.Clamp(..., 10, 1000); delta lowers the bound to 0.1. Recompute velocity
            // and tick distance here so SV > 10 behaves identically to delta.
            var timingPoint = controlPointInfo.TimingPointAt(StartTime);

            double sliderVelocityAsBeatLength = -100 / SliderVelocityMultiplier;
            double bpmMultiplier = sliderVelocityAsBeatLength < 0
                ? Math.Clamp((float)-sliderVelocityAsBeatLength, 0.1f, 1000) / 100.0
                : 1;
            double effectiveBeatLength = timingPoint.BeatLength * bpmMultiplier;
            double velocity = 100 * difficulty.SliderMultiplier / effectiveBeatLength;
            double scoringDistance = velocity * timingPoint.BeatLength;
            double tickDistance = GenerateTicks
                ? (scoringDistance / difficulty.SliderTickRate * TickDistanceMultiplier)
                : double.PositiveInfinity;

            velocity_property.SetValue(this, velocity);
            tick_distance_property.SetValue(this, tickDistance);
        }

        /// <summary>
        /// Widens a stock slider's velocity bindable in place (used for editor-placed sliders,
        /// which are created by core composition tools and cannot be DeltaSlider instances).
        /// </summary>
        public static void UncapVelocityBindable(Slider slider)
        {
            slider.SliderVelocityMultiplierBindable.MinValue = 0;
            slider.SliderVelocityMultiplierBindable.MaxValue = 1000;
        }

        private static readonly PropertyInfo velocity_property = typeof(Slider).GetProperty(nameof(Slider.Velocity))
                                                                  ?? throw new MissingMemberException(nameof(Slider), nameof(Slider.Velocity));

        private static readonly PropertyInfo tick_distance_property = typeof(Slider).GetProperty(nameof(Slider.TickDistance))
                                                                     ?? throw new MissingMemberException(nameof(Slider), nameof(Slider.TickDistance));
    }
}
