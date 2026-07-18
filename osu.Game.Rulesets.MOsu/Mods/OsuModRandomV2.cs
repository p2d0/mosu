// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Screens;
using osu.Game.Screens.Select;
using osu.Framework.Localisation;
using osu.Framework.Utils;
using osu.Framework.Logging;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.MOsu.Beatmaps;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.MOsu.UI;
using osu.Game.Rulesets.MOsu.Utils;
using osu.Game.Rulesets.UI;
using osu.Framework.Graphics;
using osuTK;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Input.StateChanges;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Overlays.Settings;

namespace osu.Game.Rulesets.MOsu.Mods
{
    /// <summary>
    /// Mod that randomises the positions of the <see cref="HitObject"/>s
    /// </summary>
    public partial class OsuModRandomV2 : ModRandom, IApplicableToBeatmap, IApplicableToDrawableRuleset<OsuHitObject>
    {

        public override string Name => "RandomV2";
        public override string Acronym => "RDV2";
        public override ModType Type => ModType.Conversion;
        public override IconUsage? Icon => OsuIcon.ModRandom;
        public override double ScoreMultiplier => 1;
        public override LocalisableString Description => "It never gets boring!";

        public override Type[] IncompatibleMods => base.IncompatibleMods.Append(typeof(OsuModTargetPractice)).ToArray();



        [SettingSource("Edit mod live in game", "", SettingControlType = typeof(PlayAutoplayButton))]
        public Bindable<bool> PlayAutoplay { get; } = new BindableBool(false);

        [SettingSource("Remove stacks", "Remove stacks")]
        public Bindable<bool> RemoveStacks { get; } = new BindableBool(false);

        [SettingSource("Aim Distance Multiplier", "How much bigger the distance")]
        public BindableFloat AimDistanceMultiplier { get; } = new BindableFloat(1)
        {
            MinValue = 0.1f,
            MaxValue = 10,
            Precision = 0.1f
        };


        [SettingSource("Exponential jumps", "", SettingControlType = typeof(ExpoJumpsSetting))]
        public BindableBool ExpoJumps { get; } = new BindableBool(false);

        [SettingSource("Stream Distance Multiplier", "How much bigger the distance")]
        public BindableFloat StreamDistanceMultiplier { get; } = new BindableFloat(1)
        {
            MinValue = 0.1f,
            MaxValue = 10,
            Precision = 0.1f
        };

        [SettingSource("Exponential streams", "Larger stream spacing receives diminishing distance increases")]
        public BindableBool PowerStreams { get; } = new BindableBool(false);

        [SettingSource("Aim/Stream Divisor", "Divisor below which circles will be considered aim")]
        public BindableInt Divisor { get; } = new BindableInt(2)
        {
            MinValue = 1,
            MaxValue = 16,
            Default = 2,
        };


        [SettingSource("Custom angle", "Custom angle")]
        public Bindable<bool> CustomAngle { get; } = new BindableBool(false);

        public enum AngleEnum
        {
            Star,
            FourtyFive,
            Ninety,
            Custom
        }

        public float GetAngleValue()
        {
            return AngleValues[Angle.Value];
        }

        private static readonly Dictionary<AngleEnum, float> AngleValues = new Dictionary<AngleEnum, float>
        {
            { AngleEnum.Star, 2.51327f },   // ~36 degrees in radians
            { AngleEnum.FourtyFive, 0.785398f }, // 45 degrees in radians
            { AngleEnum.Ninety, 1.57079f } // 90 degrees in radians
        };

        // 3. Angle: Visible when CustomAngle is true
        [SettingSource("Angle", "Angle selector", SettingControlType = typeof(AngleEnumSetting))]
        public Bindable<AngleEnum> Angle { get; } = new Bindable<AngleEnum>
        {
            Default = AngleEnum.Star,
        };

        // 1. AngleSharpness: Hidden when CustomAngle is true
        [SettingSource("Angle sharpness", "How sharp angles should be", SettingControlType = typeof(AngleSharpnessSetting))]
        public BindableFloat AngleSharpness { get; } = new BindableFloat(7)
        {
            MinValue = 1,
            MaxValue = 10,
            Precision = 0.1f
        };

        // 2. StreamAngleSharpness: Hidden when CustomAngle is true
        [SettingSource("Stream Angle sharpness", "How sharp angles should be", SettingControlType = typeof(AngleSharpnessSetting))]
        public BindableFloat StreamAngleSharpness { get; } = new BindableFloat(7)
        {
            MinValue = 1,
            MaxValue = 10,
            Precision = 0.1f
        };

        public Bindable<bool> ExtendPlayArea { get; } = new BindableBool(false);

        public Bindable<bool> InfinitePlayArea { get; } = new BindableBool(false);

        private static readonly float playfield_diagonal = MOsuPlayfield.BASE_SIZE.LengthFast;

        private Random random = null!;
        private ConditionalWeakTable<OsuHitObject, object> originalPositions = new();
        private ConditionalWeakTable<Slider, object> originalSliderPaths = new();

        private void restoreOriginals(OsuBeatmap osuBeatmap)
        {
            foreach (var obj in osuBeatmap.HitObjects.OfType<OsuHitObject>())
            {
                if (!originalPositions.TryGetValue(obj, out var pos))
                {
                    pos = obj.Position;
                    originalPositions.Add(obj, pos);
                }
                obj.Position = (Vector2)pos;

                if (obj is Slider slider)
                {
                    if (!originalSliderPaths.TryGetValue(slider, out var pathState))
                    {
                        pathState = slider.Path.ControlPoints.Select(p => p.Position).ToArray();
                        originalSliderPaths.Add(slider, pathState);
                    }
                    var pts = (Vector2[])pathState;
                    for (int j = 0; j < slider.Path.ControlPoints.Count && j < pts.Length; j++)
                        slider.Path.ControlPoints[j].Position = pts[j];
                }
            }
        }

        public void ApplyToBeatmap(IBeatmap beatmap)
        {
            if (beatmap is not OsuBeatmap osuBeatmap)
                return;



            restoreOriginals(osuBeatmap);

            Seed.Value ??= RNG.Next();

            random = new Random((int)Seed.Value);

            var positionInfos = OsuHitObjectGenerationUtils.GeneratePositionInfos(osuBeatmap.HitObjects);

            // Offsets the angles of all hit objects in a "section" by the same amount.
            float sectionOffset = 0;

            // Whether the angles are positive or negative (clockwise or counter-clockwise flow).
            bool flowDirection = false;
            var originalDistance = 0f;
            for (int i = 0; i < positionInfos.Count; i++)
            {
                originalDistance = positionInfos[i].DistanceFromPrevious;
                if(originalDistance <= 10 && RemoveStacks.Value){
                    positionInfos[i].DistanceFromPrevious += 50;
                };
                if (isStream(osuBeatmap, positionInfos,i, originalDistance))
                {
                    if(PowerStreams.Value)
                        positionInfos[i].DistanceFromPrevious = getExpoJumpsDistance(positionInfos[i].DistanceFromPrevious, StreamDistanceMultiplier.Value);
                    else
                        positionInfos[i].DistanceFromPrevious *= StreamDistanceMultiplier.Value;
                }
                else
                {
                    // if(CustomAngle.Value)
                    //     positionInfos[i].DistanceFromPrevious = SquareDistance.Value;
                    // else
                    if (ExpoJumps.Value)
                        positionInfos[i].DistanceFromPrevious = getExpoJumpsDistance(positionInfos[i].DistanceFromPrevious, AimDistanceMultiplier.Value);
                    else
                        positionInfos[i].DistanceFromPrevious *= MathF.Pow(AimDistanceMultiplier.Value, 1f - positionInfos[i].DistanceFromPrevious / 640f);

                    // if(AimDistanceMultiplier.Value >= 1)
                    //     positionInfos[i].DistanceFromPrevious *= MathF.Pow(AimDistanceMultiplier.Value, 1f - positionInfos[i].DistanceFromPrevious / 640f);
                    // else
                    //     positionInfos[i].DistanceFromPrevious *= MathF.Pow(AimDistanceMultiplier.Value, positionInfos[i].DistanceFromPrevious / 640f);

                }
                if (shouldStartNewSection(osuBeatmap, positionInfos, i))
                {
                    sectionOffset = isStream(osuBeatmap, positionInfos,i, originalDistance) ? getRandomOffsetStream(0.002f) : getRandomOffset(0.002f);
                    flowDirection = !flowDirection;
                }

                if (positionInfos[i].HitObject is Slider slider && random.NextDouble() < 0.5)
                {
                    OsuHitObjectGenerationUtils.FlipSliderInPlaceHorizontally(slider);
                }

                if (i == 0)
                {
                    positionInfos[i].DistanceFromPrevious = (float)(random.NextDouble() * MOsuPlayfield.BASE_SIZE.Y / 2);
                    // Logger.Log($"DistanceFromPrevious i=0 {positionInfos[i].DistanceFromPrevious}");
                    positionInfos[i].RelativeAngle = (float)(random.NextDouble() * 2 * Math.PI - Math.PI);
                }
                else
                {
                    // Offsets only the angle of the current hit object if a flow change occurs.
                    float flowChangeOffset = 0;

                    // Offsets only the angle of the current hit object.
                    float oneTimeOffset = isStream(osuBeatmap, positionInfos,i, originalDistance) ? getRandomOffsetStream(0.002f) : getRandomOffset(0.002f);

                    if (shouldApplyFlowChange(positionInfos, i))
                    {
                        flowChangeOffset = isStream(osuBeatmap, positionInfos,i, originalDistance) ? getRandomOffsetStream(0.002f) : getRandomOffset(0.002f);
                        flowDirection = !flowDirection;
                    }

                    float totalOffset =
                        // sectionOffset and oneTimeOffset should mainly affect patterns with large spacing.
                        (sectionOffset + oneTimeOffset) * positionInfos[i].DistanceFromPrevious +
                        // flowChangeOffset should mainly affect streams.
                        flowChangeOffset * (playfield_diagonal - positionInfos[i].DistanceFromPrevious);

                    // Logger.Log($"totalOffset i={i} {totalOffset}");
                    positionInfos[i].RelativeAngle = isStream(osuBeatmap, positionInfos,i, originalDistance) ?
                        getRelativeTargetAngleStream(positionInfos[i].DistanceFromPrevious, totalOffset, flowDirection) :
                        getRelativeTargetAngle(positionInfos[i].DistanceFromPrevious, totalOffset, flowDirection);
                    // Logger.Log($"Distance from previous i={i} {positionInfos[i].DistanceFromPrevious}");
                    // Logger.Log($"RelativeAngle i={i} {positionInfos[i].RelativeAngle}");
                }
            }

            osuBeatmap.HitObjects = OsuHitObjectGenerationUtils.RepositionHitObjects(positionInfos,true,ExtendPlayArea.Value,InfinitePlayArea.Value);
            // var updatedPositionInfos = OsuHitObjectGenerationUtils.GeneratePositionInfos(osuBeatmap.HitObjects);
            // var count = 0;
            // var totalDistanceDifferece =  0f;
            // for (int i = 0; i < positionInfos.Count; i++)
            // {
            //     if(positionInfos[i].DistanceFromPrevious - updatedPositionInfos[i].DistanceFromPrevious > 50)
            //     {
            //         count++;
            //         Logger.Log($"Position is more than 50 off i={i} {positionInfos[i].DistanceFromPrevious} {updatedPositionInfos[i].DistanceFromPrevious}");
            //         Logger.Log($"Updated RelativeAngle i={i} {positionInfos[i].RelativeAngle}");

            //     }
            //     totalDistanceDifferece += positionInfos[i].DistanceFromPrevious - updatedPositionInfos[i].DistanceFromPrevious;
            //     // Logger.Log($"Updated DistanceFromPrevious i={i} {positionInfos[i].DistanceFromPrevious}");
            //     // Logger.Log($"Updated RelativeAngle i={i} {positionInfos[i].RelativeAngle}");
            // }
            // Logger.Log($"Count (Lower is better) {count}");
            // Logger.Log($"TotalDistanceDifferece (Lower is better) {totalDistanceDifferece}");
        }

        private float getExpoJumpsDistance(float distance, float multiplier) {
            float M = multiplier;

            if (distance <= 0f) return 0f;
            if (M <= 1.0f) return distance;

            float baseRapidGrowthCap = 800f;
            float baseSlowHeadroomCap = 100f;
            float slowDecayRate = 0.0005f;
            float tuningFactor = 5.3f;
            float bonusAmplifier = 4f;
            float transitionThresholdM = 10f;

            // Calculate the cap scaling factor so that at M = 10, the total cap scales by 1.2x (yielding 600).
            // baseline M = 4 has scale 1.0.
            float capScale = 1f + (M - 4f) * 0.03333f;
        
            // Apply the dynamic scale to both plateau caps
            float rapidGrowthCap = baseRapidGrowthCap * capScale;
            float slowHeadroomCap = baseSlowHeadroomCap * capScale;

            // Calculate what the Expo Jumps initial slope would be at M = 10.
            float expoSlopeAtTen = 1f + 9f * bonusAmplifier;

            // Scale target steepness proportionally based on current multiplier M.
            float targetInitialSlope = tuningFactor * M * (expoSlopeAtTen / 10f);

            // Solve for the rapid decay rate (k1) to guarantee the takeoff steepness matches:
            // Slope = (H1 * k1) + (H2 * k2)  =>  k1 = (targetInitialSlope - (H2 * k2)) / H1
            float targetRapidSlope = targetInitialSlope - (slowHeadroomCap * slowDecayRate);
        
            // Ensure k1 stays positive and stable
            float k1 = MathF.Max(0.0001f, targetRapidSlope / rapidGrowthCap);
            float k2 = slowDecayRate;

            // Phase 1: Rapidly climb towards the first plateau (H1). 
            // Highly active at d = 50, but completely saturated/dead at d = 450.
            float rapidPart = rapidGrowthCap * (1f - MathF.Exp(-k1 * distance));

            // Phase 2: Slowly crawl up the rest of the way towards the second plateau (H2).
            // Operates as a super tiny, steady drift at d = 450.
            float slowPart = slowHeadroomCap * (1f - MathF.Exp(-k2 * distance));

            float rawSaturatedDistance = rapidPart + slowPart;

            // Smoothly blend from the raw linear distance (y = d) to the saturated distance (y = rawSaturatedDistance)
            // as M goes from 1.0 to the transitionThresholdM (2.0) to avoid sharp steps in sensitivity.
            if (M < transitionThresholdM) {
                float t = (M - 1.0f) / (transitionThresholdM - 1.0f);
                float blendFactor = t * t * (3.0f - 2.0f * t); // Smoothstep interpolation
                return (1.0f - blendFactor) * distance + blendFactor * rawSaturatedDistance;
            }

            // Return the fully saturated curve value for multipliers >= transitionThresholdM (2.0)
            return rawSaturatedDistance;
        }

        private bool isStream(OsuBeatmap osuBeatmap, List<OsuHitObjectGenerationUtils.ObjectPositionInfo> positionInfos,int i, float originalDistance)
        {
            var beatLength = osuBeatmap.ControlPointInfo.TimingPointAt(positionInfos[i].HitObject.StartTime).BeatLength;
            if(i+1 < positionInfos.Count && positionInfos[i].HitObject is HitObject circle && positionInfos[i+1].HitObject is HitObject nextCircle){
                var isStream = nextCircle.StartTime - circle.StartTime + 1 < beatLength / Divisor.Value;
                if(!isStream && i > 0 && positionInfos[i-1] != null && positionInfos[i-1].HitObject is HitObject previousCircle)
                    return circle.StartTime - previousCircle.StartTime + 1 < beatLength / Divisor.Value;
            }

            return true;
        }

        // private int Moved = 0;

        private OsuInputManager inputManager = null!;

        public void ApplyToDrawableRuleset(DrawableRuleset<OsuHitObject> drawableRuleset)
        {
            // Grab the input manager to disable the user's cursor, and for future use
            inputManager = ((DrawableOsuRuleset)drawableRuleset).KeyBindingInputManager;
        }

        // public void Update(Playfield playfield)
        // {
        //     var padding = 200;
        //     // Get current cursor position
        //     var cursorPos = playfield.Cursor.AsNonNull().ActiveCursor.DrawPosition;
        //     if(cursorPos.X > (playfield.DrawPosition.X + playfield.DrawWidth) || cursorPos.Y > (playfield.DrawPosition.Y + playfield.DrawHeight ))
        //     {
        //         Logger.Log($"Cursor position is out of bounds: {cursorPos}");
        //         var offsetX = cursorPos.X > playfield.DrawWidth ? -cursorPos.X + padding : 0;
        //         var offsetY = cursorPos.Y < playfield.DrawHeight ? -cursorPos.Y + padding : 0;
        //         // inputManager.MoveMouseTo(new Vector2(1000,500));
        //         playfield.Cursor.ActiveCursor.MoveTo(new Vector2(200,200), 0, Easing.None);
        //         // new MousePositionAbsoluteInput { Position = playfield.ToScreenSpace(new Vector2(200,200)) }.Apply(inputManager.CurrentState, inputManager);
        //         playfield.MoveTo(new Vector2(offsetX, offsetY), 0, Easing.None);
        //         return;
        //     }

        //     if(cursorPos.X < 0 || cursorPos.Y < 0)
        //     {
        //         Logger.Log($"Cursor position is out of bounds: {cursorPos}");
        //         var offsetX = cursorPos.X < 0 ? -cursorPos.X - padding : 0;
        //         var offsetY = cursorPos.Y < 0 ? -cursorPos.Y - padding : 0;
        //         // inputManager.MoveMouseTo(new Vector2(1000,500));
        //         // playfield.Cursor.ActiveCursor.MoveTo(new Vector2(200,200), 0, Easing.None);
        //         // new MousePositionAbsoluteInput { Position = playfield.ToScreenSpace(new Vector2(200,200)) }.Apply(inputManager.CurrentState, inputManager);
        //         playfield.MoveTo(new Vector2(offsetX, offsetY), 0, Easing.None);
        //         // playfield.Cursor.ActiveCursor.
        //         // Moved += 1;
        //     }

        //     // Logger.Log($"Cursor position: {cursorPos}");
        //     // Logger.Log($"Playfield: {playfield.DrawWidth}x{playfield.DrawHeight}");

        //     // Calculate screen center
        //     // var screenCenter = new Vector2(playfield.DrawWidth / 2, playfield.DrawHeight / 2);

        //     // // Calculate cursor's offset from center
        //     // var cursorOffset = cursorPos - screenCenter;

        //     // // Calculate desired playfield offset (scaled by extension factor)
        //     // var extension = ExtendPlayArea.Value ? PlayAreaExtension.Value : 0;
        //     // var targetOffset = new Vector2(
        //     //     extension * (cursorOffset.X / screenCenter.X),
        //     //     extension * (cursorOffset.Y / screenCenter.Y)
        //     // );

        //     // // Apply the offset to the playfield
        // }

        private float getRandomOffset(float stdDev)
        {
            // Range: [0.5, 2]
            // Higher angle sharpness -> lower multiplier
            float customMultiplier = (1.5f * AngleSharpness.MaxValue - AngleSharpness.Value) / (1.5f * AngleSharpness.MaxValue - AngleSharpness.Default);

            return OsuHitObjectGenerationUtils.RandomGaussian(random, 0, stdDev * customMultiplier);
        }

        private float getRandomOffsetStream(float stdDev)
        {
            // Range: [0.5, 2]
            // Higher angle sharpness -> lower multiplier
            float customMultiplier = (1.5f * StreamAngleSharpness.MaxValue - StreamAngleSharpness.Value) / (1.5f * StreamAngleSharpness.MaxValue - StreamAngleSharpness.Default);

            return OsuHitObjectGenerationUtils.RandomGaussian(random, 0, stdDev * customMultiplier);
        }

        /// <param name="targetDistance">The target distance between the previous and the current <see cref="OsuHitObject"/>.</param>
        /// <param name="offset">The angle (in rad) by which the target angle should be offset.</param>
        /// <param name="flowDirection">Whether the relative angle should be positive or negative.</param>
        private float getRelativeTargetAngle(float targetDistance, float offset, bool flowDirection)
        {
            // Range: [0.1, 1]
            float angleSharpness = AngleSharpness.Value / AngleSharpness.MaxValue;
            // Range: [0, 0.9]
            float angleWideness = 1 - angleSharpness;

            // Range: [-60, 30]
            float customOffsetX = angleSharpness * 100 - 70;
            // Range: [-0.075, 0.15]
            float customOffsetY = angleWideness * 0.25f - 0.075f;

            targetDistance += customOffsetX;
            float angle = (float)(2.16 / (1+ 200 * Math.Exp(0.036 * (targetDistance - 310 + customOffsetX))) + 0.5);
            angle += offset + customOffsetY;

            float relativeAngle = (float)Math.PI - angle;
            // Logger.Log($"relativeAngle {relativeAngle} angle {angle}");

            if(CustomAngle.Value && Angle.Value != AngleEnum.Custom)
                relativeAngle = GetAngleValue();

            return flowDirection ? -relativeAngle : relativeAngle;
        }

        /// <param name="targetDistance">The target distance between the previous and the current <see cref="OsuHitObject"/>.</param>
        /// <param name="offset">The angle (in rad) by which the target angle should be offset.</param>
        /// <param name="flowDirection">Whether the relative angle should be positive or negative.</param>
        private float getRelativeTargetAngleStream(float targetDistance, float offset, bool flowDirection)
        {
            // Range: [0.1, 1]
            float angleSharpness = StreamAngleSharpness.Value / StreamAngleSharpness.MaxValue;
            // Range: [0, 0.9]
            float angleWideness = 1 - angleSharpness;

            // Range: [-60, 30]
            float customOffsetX = angleSharpness * 100 - 70;
            // Range: [-0.075, 0.15]
            float customOffsetY = angleWideness * 0.25f - 0.075f;

            targetDistance += customOffsetX;
            float angle = (float)(2.16 / (1+ 200 * Math.Exp(0.036 * (targetDistance - 310 + customOffsetX))) + 0.5);
            angle += offset + customOffsetY;

            float relativeAngle = (float)Math.PI - angle;

            if(CustomAngle.Value && Angle.Value != AngleEnum.Custom)
                relativeAngle = GetAngleValue();

            return flowDirection ? -relativeAngle : relativeAngle;
        }

        /// <returns>Whether a new section should be started at the current <see cref="OsuHitObject"/>.</returns>
        private bool shouldStartNewSection(OsuBeatmap beatmap, IReadOnlyList<OsuHitObjectGenerationUtils.ObjectPositionInfo> positionInfos, int i)
        {
            if (Angle.Value == AngleEnum.Custom)
                return false;
            if (i == 0)
                return true;

            // Exclude new-combo-spam and 1-2-combos.
            bool previousObjectStartedCombo = positionInfos[Math.Max(0, i - 2)].HitObject.IndexInCurrentCombo > 1 &&
                                              positionInfos[i - 1].HitObject.NewCombo;
            bool previousObjectWasOnDownbeat = OsuHitObjectGenerationUtils.IsHitObjectOnBeat(beatmap, positionInfos[i - 1].HitObject, true);
            bool previousObjectWasOnBeat = OsuHitObjectGenerationUtils.IsHitObjectOnBeat(beatmap, positionInfos[i - 1].HitObject);

            return (previousObjectStartedCombo && random.NextDouble() < 0.6f) ||
                   previousObjectWasOnDownbeat ||
                   (previousObjectWasOnBeat && random.NextDouble() < 0.4f);
        }

        /// <returns>Whether a flow change should be applied at the current <see cref="OsuHitObject"/>.</returns>
        private bool shouldApplyFlowChange(IReadOnlyList<OsuHitObjectGenerationUtils.ObjectPositionInfo> positionInfos, int i)
        {
            // Exclude new-combo-spam and 1-2-combos.
            bool previousObjectStartedCombo = positionInfos[Math.Max(0, i - 2)].HitObject.IndexInCurrentCombo > 1 &&
                                              positionInfos[i - 1].HitObject.NewCombo;

            return previousObjectStartedCombo && random.NextDouble() < 0.6f;
        }

        public partial class AngleSharpnessSetting : SettingsSlider<float>
        {
            protected override void LoadComplete()
            {
                base.LoadComplete();
                if (SettingSourceObject is OsuModRandomV2 mod)
                {
                    mod.Angle.BindValueChanged(val =>
                    {
                        if (val.NewValue == AngleEnum.Custom) Show(); else Hide();
                    }, true);
                }
            }
        }

        public partial class AngleEnumSetting : SettingsEnumDropdown<AngleEnum>
        {
            protected override void LoadComplete()
            {
                base.LoadComplete();
                if (SettingSourceObject is OsuModRandomV2 mod)
                {
                    mod.CustomAngle.BindValueChanged(val =>
                    {
                        if (val.NewValue) Show(); else Hide();
                    }, true);
                }
            }
        }

    public partial class PlayAutoplayButton : SettingsItem<bool>
    {
        private PlayButton playButton = null!;

        protected override Drawable CreateControl() => playButton = new PlayButton
        {
            RelativeSizeAxes = Axes.X,
        };

        public override LocalisableString LabelText
        {
            get => playButton.Text;
            set => playButton.Text = value;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            playButton.SetMod(SettingSourceObject as OsuModRandomV2);
        }

        private partial class PlayButton : Container, IHasCurrentValue<bool>
        {
            private readonly Bindable<bool> current = new Bindable<bool>();
            public Bindable<bool> Current
            {
                get => current;
                set
                {
                    current.UnbindBindings();
                    current.BindTo(value);
                }
            }

            public LocalisableString Text
            {
                get => button.Text;
                set => button.Text = value;
            }

            private RoundedButton button = null!;
            private OsuModRandomV2? mod;

            [Resolved]
            private IPerformFromScreenRunner screenRunner { get; set; } = null!;

            public PlayButton()
            {
                Height = 20;
                Add(button = new RoundedButton
                {
                    RelativeSizeAxes = Axes.X,
                    Height = 20,
                });
            }

            public void SetMod(OsuModRandomV2? mod)
            {
                this.mod = mod;
            }

            protected override void LoadComplete()
            {
                base.LoadComplete();
                button.Action = () =>
                {
                    Current.Value = false;

                    if (mod != null)
                    {
                        var autoplay = new OsuModAutoplay();
                        screenRunner.PerformFromScreen(screen =>
                        {
                            var modsProp = screen.GetType().GetProperty("Mods");
                            if (modsProp == null) return;

                            var modsList = modsProp.GetValue(screen);
                            var valueProp = modsList?.GetType().GetProperty("Value");
                            var currentMods = valueProp?.GetValue(modsList) as IReadOnlyList<Mod> ?? Array.Empty<Mod>();

                            // Save original mods (without autoplay), matching SoloSongSelect.OnStart behaviour
                            var savedMods = currentMods.Select(m => m.DeepClone()).ToArray();

                            // Add autoplay mod (remove existing autoplay first)
                            var newMods = currentMods.Where(m => !(m is ModAutoplay)).Concat(new[] { autoplay }).ToArray();
                            valueProp?.SetValue(modsList, newMods);

                            // Start the game via OnStart — this will save modsAtGameplayStart
                            // (which now includes autoplay). We fix this by restoring savedMods
                            // after OnStart runs, so revertMods will restore the correct mods.
                            var onStart = screen.GetType().GetMethod("OnStart", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            onStart?.Invoke(screen, null);

                            // Overwrite modsAtGameplayStart so revertMods restores original mods (without autoplay)
                            var modsAtStartProp = screen.GetType().GetField("modsAtGameplayStart", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            modsAtStartProp?.SetValue(screen, savedMods);
                        }, new[] { typeof(SoloSongSelect) });
                    }
                };
            }
        }
    }
    }

}
