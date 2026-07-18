// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Graphics;
using osu.Game.Overlays.Settings;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.MOsu.Beatmaps;
using osu.Game.Rulesets.MOsu.UI;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Mods
{
    /// <summary>
    /// Generates hit circles in a square spiral pattern.
    /// </summary>
    public partial class OsuModCircleGeneration : Mod, IApplicableToBeatmap
    {
        public override string Name => "Circle Generation";
        public override string Acronym => "CG";
        public override ModType Type => ModType.Conversion;
        public override double ScoreMultiplier => 1;
        public override LocalisableString Description => "Generate hit circles in a square pattern.";
        public override Type[] IncompatibleMods => new[] { typeof(ModHardRock) };

        [SettingSource("Divisor", "Divisor selector", SettingControlType = typeof(CGIntSetting))]
        public BindableInt Divisor { get; } = new BindableInt(2)
        {
            MinValue = 1,
            MaxValue = 16,
            Default = 2,
        };

        [SettingSource("Distance", "Distance", SettingControlType = typeof(CGIntSetting))]
        public BindableInt Distance { get; } = new BindableInt(50)
        {
            MinValue = 1,
            MaxValue = 200,
            Default = 50
        };

        [SettingSource("BreakDistance", "Break Distance", SettingControlType = typeof(CGIntSetting))]
        public BindableInt BreakDistance { get; } = new BindableInt(50)
        {
            MinValue = 1,
            MaxValue = 200,
            Default = 50
        };

        [SettingSource("Include breaks?", "Add breaks", SettingControlType = typeof(CGBoolSetting))]
        public Bindable<bool> Break { get; } = new BindableBool(false);

        // Visible when Break is true
        [SettingSource("Break interval", "Break every x objects", SettingControlType = typeof(CGBreakIntervalSetting))]
        public BindableInt BreakInterval { get; } = new BindableInt(100)
        {
            MinValue = 1,
            MaxValue = 500,
            Default = 100,
        };

        [SettingSource("Break duration (in hitobjects)", "Break for x circles", SettingControlType = typeof(CGIntSetting))]
        public BindableInt BreakObjects { get; } = new BindableInt(25)
        {
            MinValue = 1,
            MaxValue = 100,
            Default = 25,
        };

        [SettingSource("Full map?", "Full map or just a part", SettingControlType = typeof(CGBoolSetting))]
        public Bindable<bool> FullMap { get; } = new BindableBool(true);

        // Visible when FullMap is false
        [SettingSource("Offset (in circles)", "Offset in circles", SettingControlType = typeof(CGOffsetSetting))]
        public BindableInt Offset { get; } = new BindableInt(0)
        {
            MinValue = 0,
            MaxValue = 5000,
            Precision = 1,
            Default = 0,
        };

        // Visible when FullMap is false
        [SettingSource("Hitobjects count", "How many circles", SettingControlType = typeof(CGCountSetting))]
        public BindableInt Count { get; } = new BindableInt(0)
        {
            MinValue = 0,
            MaxValue = 5000,
            Precision = 100,
            Default = 0,
        };

        [SettingSource("Increasing?", "3 then 6 then 9 circles etc", SettingControlType = typeof(CGBoolSetting))]
        public Bindable<bool> Increasing { get; } = new BindableBool(false);

        [SettingSource("Kickslider", "Replace break gap with a repeating slider", SettingControlType = typeof(CGBoolSetting))]
        public Bindable<bool> Kickslider { get; } = new BindableBool(false);

        public void ApplyToBeatmap(IBeatmap beatmap)
        {
            makeMapSquare(beatmap);
        }

        private Slider createKickslider(double startTime, double duration, Vector2 position, OsuBeatmap osuBeatmap, OsuHitObject firstHitObject, double beatLength)
        {
            int repeatCount = BreakObjects.Value - 1;
            var timingPoint = osuBeatmap.ControlPointInfo.TimingPointAt(startTime);
            double velocity = 100 * osuBeatmap.Difficulty.SliderMultiplier / beatLength;
            double targetDistance = (duration * velocity) / (repeatCount + 1);

            var slider = new Slider
            {
                Position = position,
                RepeatCount = repeatCount,
                StartTime = startTime,
                Path = new SliderPath(new[]
                {
                    new PathControlPoint(new Vector2(0, 0)),
                    new PathControlPoint(new Vector2((float)targetDistance, 0))
                })
            };
            slider.Samples = firstHitObject.Samples;
            slider.ApplyDefaults(osuBeatmap.ControlPointInfo, osuBeatmap.Difficulty);
            return slider;
        }

        private void makeMapSquare(IBeatmap beatmap)
        {
            if (beatmap is not OsuBeatmap osuBeatmap)
                return;

            var firstHitObject = beatmap.HitObjects.OfType<OsuHitObject>().FirstOrDefault();
            if (firstHitObject == null)
                return;

            var lastTime = beatmap.HitObjects.Last().StartTime;
            var firstTime = firstHitObject.StartTime;

            if (!FullMap.Value)
                firstTime += Offset.Value * (osuBeatmap.ControlPointInfo.TimingPointAt(firstTime).BeatLength / Divisor.Value);

            var hitObjects = new List<OsuHitObject>();
            var spacing = Distance.Value;

            int circlesSinceLastBreak = 0;
            int breakInterval = 3;
            const int breakIncreaseAmount = 1;

            do
            {
                int cornerIndex = hitObjects.Count % 4;
                float currentSpacing = spacing;
                Vector2 position;

                if (hitObjects.Count == 0)
                {
                    position = Vector2.Zero;
                }
                else
                {
                    Vector2 prevPosition = hitObjects[hitObjects.Count - 1].Position;
                    switch (cornerIndex)
                    {
                        case 1:
                            position = prevPosition + new Vector2(currentSpacing, 0);
                            break;
                        case 2:
                            position = prevPosition + new Vector2(0, currentSpacing);
                            break;
                        case 3:
                            position = prevPosition + new Vector2(-currentSpacing, 0);
                            break;
                        case 0:
                            position = prevPosition + new Vector2(0, -currentSpacing);
                            break;
                        default:
                            position = Vector2.Zero;
                            break;
                    }
                }

                var circle = new HitCircle
                {
                    Position = position,
                    NewCombo = cornerIndex == 0,
                    Samples = firstHitObject.Samples
                };

                circle.ApplyDefaults(osuBeatmap.ControlPointInfo, osuBeatmap.Difficulty);
                var previousCircle = hitObjects.Count > 0 ? hitObjects[hitObjects.Count - 1] : null;
                circle.UpdateComboInformation(previousCircle);

                double nextStartTime;

                if (hitObjects.Count == 0)
                {
                    nextStartTime = osuBeatmap.ControlPointInfo.GetClosestSnappedTime(firstTime);
                }
                else
                {
                    var beatLength = osuBeatmap.ControlPointInfo.TimingPointAt(previousCircle!.StartTime)!.BeatLength / Divisor.Value;
                    var fullBeatLength = osuBeatmap.ControlPointInfo.TimingPointAt(previousCircle!.StartTime)!.BeatLength;
                    nextStartTime = previousCircle.StartTime + beatLength;

                    if (Increasing.Value)
                    {
                        if (circlesSinceLastBreak >= breakInterval)
                        {
                            double breakDuration = beatLength * BreakObjects.Value;
                            if (Kickslider.Value)
                            {
                                var kickslider = createKickslider(previousCircle.StartTime + beatLength, breakDuration, previousCircle.Position + new Vector2(spacing, 0), osuBeatmap, firstHitObject, fullBeatLength);
                                hitObjects.Add(kickslider);
                                nextStartTime = kickslider.EndTime + beatLength;
                            }
                            else
                            {
                                nextStartTime += breakDuration;
                            }
                            breakInterval += breakIncreaseAmount;
                            circlesSinceLastBreak = 0;
                        }
                    }
                    else if (Break.Value && hitObjects.Count % BreakInterval.Value == 0)
                    {
                        double breakDuration = beatLength * BreakObjects.Value;
                        if (Kickslider.Value)
                        {
                            var kickslider = createKickslider(previousCircle.StartTime + beatLength, breakDuration, previousCircle.Position + new Vector2(spacing, 0), osuBeatmap, firstHitObject, fullBeatLength);
                            hitObjects.Add(kickslider);
                            nextStartTime = kickslider.EndTime + beatLength;
                        }
                        else
                        {
                            nextStartTime += breakDuration;
                            circle.Position += new Vector2(BreakDistance.Value, BreakDistance.Value);
                        }
                    }
                }

                circle.StartTime = osuBeatmap.ControlPointInfo.GetClosestSnappedTime(nextStartTime);
                circle.TimePreempt = firstHitObject.TimePreempt;
                var firstCircle = beatmap.HitObjects.OfType<HitCircle>().FirstOrDefault();
                if (firstCircle != null)
                    circle.TimeFadeIn = firstCircle.TimeFadeIn;

                if (hitObjects.Count < 3)
                    Logger.Log($"[CircleGeneration] circle[{hitObjects.Count}]: StartTime={circle.StartTime:N0} TimePreempt={circle.TimePreempt:N0} TimeFadeIn={circle.TimeFadeIn:N0}");

                hitObjects.Add(circle);

                if (Increasing.Value)
                    circlesSinceLastBreak++;

                if (!FullMap.Value && Count.Value > 0 && hitObjects.Count > Count.Value)
                    break;

            } while (hitObjects.Last().StartTime < lastTime);

            osuBeatmap.HitObjects = hitObjects;
            beatmap.Breaks.Clear();
        }

        // --- Visibility settings ---

        public partial class CGIntSetting : SettingsSlider<int> { }

        public partial class CGBoolSetting : SettingsCheckbox { }

        // Visible when Break is true
        public partial class CGBreakIntervalSetting : SettingsSlider<int>
        {
            protected override void LoadComplete()
            {
                base.LoadComplete();
                if (SettingSourceObject is OsuModCircleGeneration mod)
                {
                    var updateVisibility = new Action(() =>
                    {
                        bool show = mod.Break.Value;
                        if (show) Show(); else Hide();
                    });
                    mod.Break.BindValueChanged(_ => updateVisibility(), true);
                }
            }
        }

        // Visible when FullMap is false
        public partial class CGOffsetSetting : SettingsSlider<int>
        {
            protected override void LoadComplete()
            {
                base.LoadComplete();
                if (SettingSourceObject is OsuModCircleGeneration mod)
                {
                    mod.FullMap.BindValueChanged(val =>
                    {
                        if (!val.NewValue) Show(); else Hide();
                    }, true);
                }
            }
        }

        // Visible when FullMap is false
        public partial class CGCountSetting : SettingsSlider<int, CGObjectCountSliderBar>
        {
            protected override void LoadComplete()
            {
                base.LoadComplete();
                if (SettingSourceObject is OsuModCircleGeneration mod)
                {
                    mod.FullMap.BindValueChanged(val =>
                    {
                        if (!val.NewValue) Show(); else Hide();
                    }, true);
                }
            }
        }

        public partial class CGObjectCountSliderBar : RoundedSliderBar<int>
        {
            public override LocalisableString TooltipText => Current.Value == 0 ? "No limit" : base.TooltipText;
        }
    }
}
