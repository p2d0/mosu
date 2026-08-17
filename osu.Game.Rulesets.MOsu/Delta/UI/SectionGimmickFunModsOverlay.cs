// Ported from delta fork (osu.Game.Rulesets.Osu/UI/SectionGimmickFunModsOverlay.cs),
// adapted to read gimmick data from DeltaBeatmap and use MOsuPlayfield.
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Effects;
using osu.Framework.Graphics.Pooling;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Transforms;
using osu.Framework.Utils;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Rulesets.MOsu.Delta.Beatmaps;
using osu.Game.Rulesets.MOsu.Delta.Gimmicks;
using osu.Game.Rulesets.MOsu.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Rulesets.Osu.UI;
using osu.Game.Rulesets.Osu.UI.Cursor;
using osu.Game.Rulesets.Osu.Utils;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.MOsu.Delta.UI
{
    public partial class SectionGimmickFunModsOverlay : CompositeDrawable
    {
        private const float no_scope_min_alpha = 0.0002f;

        private static readonly Vector3 depth_camera_position = new Vector3(OsuPlayfield.BASE_SIZE.X * 0.5f, OsuPlayfield.BASE_SIZE.Y * 0.5f, -200);

        private readonly IBeatmap beatmap;
        private readonly BeatmapSectionGimmicks gimmicks;
        private readonly DrawableRuleset<OsuHitObject> drawableRuleset;
        private readonly IReadOnlyList<Mod> selectedMods;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        private readonly BindableDouble mutedVolumeAdjustment = new BindableDouble(1);

        private SectionModApplicator sectionModApplicator = null!;

        private readonly Bindable<int> currentCombo = new BindableInt();
        private float bubbleMaxSize;
        private float bubbleSize;
        private double bubbleFade;
        private PlayfieldAdjustmentContainer bubbleContainer = null!;
        private DrawablePool<BubbleDrawable> bubblePool = null!;

        private bool hasForcedMuted;
        private bool hasForcedBarrelRoll;
        private bool hasForcedNoScope;
        private bool hasForcedBloom;
        private bool hasForcedBubbles;

        private bool selectedBubbles;
        private bool selectedMuted;
        private bool selectedBarrelRoll;
        private bool selectedNoScope;
        private bool selectedBloom;
        private bool selectedMagnetised;
        private bool selectedRepel;
        private bool selectedDepth;

        private bool hasForcedMotionEffects;

        private bool initialDisplayJudgements;
        private readonly HashSet<DrawableHitObject> processedDrawables = new HashSet<DrawableHitObject>();

        [Resolved(canBeNull: true)]
        private ScoreProcessor? scoreProcessor { get; set; }

        [Resolved(canBeNull: true)]
        private Player? player { get; set; }

        public SectionGimmickFunModsOverlay(IBeatmap beatmap, DrawableRuleset<OsuHitObject> drawableRuleset, IReadOnlyList<Mod> selectedMods)
        {
            this.beatmap = beatmap;
            this.drawableRuleset = drawableRuleset;
            this.selectedMods = selectedMods;

            gimmicks = (beatmap as DeltaBeatmap)?.Gimmicks?.Sections ?? new BeatmapSectionGimmicks();

            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            selectedBubbles = hasSelectedMod<OsuModBubbles>();
            selectedMuted = hasSelectedMod<OsuModMuted>();
            selectedBarrelRoll = hasSelectedMod<OsuModBarrelRoll>();
            selectedNoScope = hasSelectedMod<OsuModNoScope>();
            selectedBloom = hasSelectedMod<OsuModBloom>();
            selectedMagnetised = hasSelectedMod<OsuModMagnetised>();
            selectedRepel = hasSelectedMod<OsuModRepel>();
            selectedDepth = hasSelectedMod<OsuModDepth>();

            hasForcedMuted = hasAnyForced(s => s.ForceMuted) && !selectedMuted;
            hasForcedBarrelRoll = hasAnyForced(s => s.ForceBarrelRoll) && !selectedBarrelRoll;
            hasForcedNoScope = hasAnyForced(s => s.ForceNoScope) && !selectedNoScope;
            hasForcedBloom = hasAnyForced(s => s.ForceBloom) && !selectedBloom;
            hasForcedBubbles = hasAnyForced(s => s.ForceBubbles) && !selectedBubbles;
            hasForcedMotionEffects = (hasAnyForced(s => s.ForceMagnetised) && !selectedMagnetised)
                                     || (hasAnyForced(s => s.ForceRepel) && !selectedRepel)
                                     || (hasAnyForced(s => s.ForceDepth) && !selectedDepth);

            sectionModApplicator = new SectionModApplicator(beatmap, selectedMods, colours);

            if (hasForcedBubbles)
            {
                OsuHitObject firstObject = drawableRuleset.Beatmap.HitObjects.OfType<OsuHitObject>().First();

                // Multiplying by 2 results in an initial size that is too large, hence 1.90 has been chosen.
                bubbleSize = (float)firstObject.Radius * 1.90f;
                bubbleFade = firstObject.TimePreempt * 2;

                // Judgements are obscured by the bubble drawables (layering).
                drawableRuleset.Playfield.DisplayJudgements.Value = false;

                bubbleContainer = drawableRuleset.CreatePlayfieldAdjustmentContainer();

                drawableRuleset.Overlays.Add(bubbleContainer);
                drawableRuleset.Overlays.Add(bubblePool = new DrawablePool<BubbleDrawable>(100));

                if (scoreProcessor != null)
                {
                    currentCombo.BindTo(scoreProcessor.Combo);
                    currentCombo.BindValueChanged(combo =>
                        bubbleMaxSize = Math.Min(1.75f, (float)(1.25 + 0.005 * combo.NewValue)), true);
                }
            }

            if (hasForcedMuted)
                drawableRuleset.Audio.AddAdjustment(AdjustableProperty.Volume, mutedVolumeAdjustment);

            if (hasForcedMotionEffects)
            {
                if (drawableRuleset.Playfield is MOsuPlayfield mosuPlayfield)
                    mosuPlayfield.FollowPoints.Hide();
            }

            initialDisplayJudgements = drawableRuleset.Playfield.DisplayJudgements.Value;
        }

        protected override void Update()
        {
            base.Update();

            applyDrawableModsOnce();
            updateSectionScopedGlobalEffects();
            updateMotionEffects();
        }

        private void applyDrawableModsOnce()
        {
            if (!drawableRuleset.Playfield.HitObjectContainer.AliveEntries.Any())
                return;

            foreach (var entry in drawableRuleset.Playfield.HitObjectContainer.AliveEntries)
            {
                DrawableHitObject drawable = entry.Value;

                if (!processedDrawables.Add(drawable))
                    continue;

                // NOTE: drawables are pooled and reused for many objects, and ApplyCustomUpdateState
                // subscriptions persist across pool reuse. Applying a mod to the pooled drawable would
                // therefore leak its effect onto every later object (map-wide). All per-drawable mods
                // are applied through SectionModApplicator's per-object guarded hooks which resolve the
                // section for the object currently held by the drawable at fire time.
                sectionModApplicator.HookSectionScopedMods(drawable);

                if (hasForcedBubbles)
                    applySectionScopedBubbles(drawable);
            }
        }

        private void applySectionScopedBubbles(DrawableHitObject drawable)
        {
            drawable.OnNewResult += (d, _) =>
            {
                if (d is not DrawableOsuHitObject drawableOsuHitObject)
                    return;

                switch (drawableOsuHitObject.HitObject)
                {
                    case Slider:
                    case SpinnerTick:
                        break;

                    default:
                        if (SectionModApplicator.ResolveSettingsForHitObject(beatmap, d.HitObject)?.ForceBubbles != true)
                            break;

                        BubbleDrawable bubble = bubblePool.Get();

                        bubble.WasHit = d.IsHit;
                        bubble.Position = getBubblePosition(drawableOsuHitObject);
                        bubble.AccentColour = d.AccentColour.Value;
                        bubble.InitialSize = new Vector2(bubbleSize);
                        bubble.FadeTime = bubbleFade;
                        bubble.MaxSize = bubbleMaxSize;

                        bubbleContainer.Add(bubble);
                        break;
                }
            };

            drawable.OnRevertResult += (d, _) =>
            {
                if (d.HitObject is SpinnerTick or Slider)
                    return;

                if (SectionModApplicator.ResolveSettingsForHitObject(beatmap, d.HitObject)?.ForceBubbles != true)
                    return;

                bubbleContainer.OfType<BubbleDrawable>().LastOrDefault()?.ClearTransforms();
                bubbleContainer.OfType<BubbleDrawable>().LastOrDefault()?.Expire(true);
            };
        }

        private static Vector2 getBubblePosition(DrawableOsuHitObject drawableObject)
        {
            switch (drawableObject)
            {
                // SliderHeads are derived from HitCircles, so they must be handled first.
                case DrawableSliderHead:
                    return drawableObject.HitObject.Position;

                // HitObject position is wrong for HitCircle due to stack leniency.
                case DrawableHitCircle:
                    return drawableObject.Position;

                default:
                    return drawableObject.HitObject.Position;
            }
        }

        private void updateSectionScopedGlobalEffects()
        {
            SectionGimmickSettings? currentSettings = resolveSettingsAtTime(Time.Current);

            updateBarrelRoll(currentSettings);
            updateMuted(currentSettings);
            updateNoScope(currentSettings);
            updateBloom(currentSettings);
            updateBubblesJudgementVisibility(currentSettings);
        }

        private void updateBarrelRoll(SectionGimmickSettings? settings)
        {
            if (!hasForcedBarrelRoll)
                return;

            bool active = settings?.ForceBarrelRoll == true;

            if (active)
            {
                double spinSpeed = Math.Clamp(settings!.BarrelRollSpinSpeed, 0.02, 12);
                float rotation = 360f * (float)(drawableRuleset.Playfield.Time.Current / 60000d * spinSpeed);

                drawableRuleset.PlayfieldAdjustmentContainer.Rotation = rotation;

                Vector2 playfieldSize = drawableRuleset.Playfield.DrawSize;
                if (playfieldSize.X > 0 && playfieldSize.Y > 0)
                {
                    float minSide = MathF.Min(playfieldSize.X, playfieldSize.Y);
                    float maxSide = MathF.Max(playfieldSize.X, playfieldSize.Y);
                    drawableRuleset.PlayfieldAdjustmentContainer.Scale = new Vector2(minSide / maxSide);
                }

                if (drawableRuleset.Playfield.Cursor != null)
                    drawableRuleset.Playfield.Cursor.ActiveCursor.Rotation = -rotation;
            }
            else
            {
                drawableRuleset.PlayfieldAdjustmentContainer.Rotation = 0;
                drawableRuleset.PlayfieldAdjustmentContainer.Scale = Vector2.One;

                if (drawableRuleset.Playfield.Cursor != null)
                    drawableRuleset.Playfield.Cursor.ActiveCursor.Rotation = 0;
            }
        }

        private void updateMuted(SectionGimmickSettings? settings)
        {
            if (!hasForcedMuted)
                return;

            if (settings?.ForceMuted == true)
            {
                int comboTarget = Math.Clamp(settings.MutedMuteComboCount, 0, 500);
                int combo = scoreProcessor?.Combo.Value ?? 0;

                double dimFactor = comboTarget == 0 ? 1 : (double)combo / comboTarget;
                dimFactor = Math.Clamp(dimFactor, 0, 1);

                mutedVolumeAdjustment.Value = 1 - dimFactor;
            }
            else
            {
                mutedVolumeAdjustment.Value = 1;
            }
        }

        private void updateNoScope(SectionGimmickSettings? settings)
        {
            if (!hasForcedNoScope)
                return;

            if (drawableRuleset.Playfield is not MOsuPlayfield mosuPlayfield || mosuPlayfield.Cursor == null)
                return;

            float alpha = 1;

            if (settings?.ForceNoScope == true)
            {
                int comboTarget = Math.Clamp(settings.NoScopeHiddenComboCount, 0, 50);
                int combo = scoreProcessor?.Combo.Value ?? 0;

                alpha = comboTarget == 0
                    ? no_scope_min_alpha
                    : Math.Max(no_scope_min_alpha, 1 - (float)combo / comboTarget);

                if (player?.IsBreakTime.Value == true)
                    alpha = 1;
            }

            mosuPlayfield.Cursor.Alpha = alpha;
            mosuPlayfield.Smoke.Alpha = alpha;
        }

        private void updateBloom(SectionGimmickSettings? settings)
        {
            if (!hasForcedBloom)
                return;

            if (drawableRuleset.Playfield.Cursor?.ActiveCursor is not OsuCursor osuCursor)
                return;

            float scale = 1;

            if (settings?.ForceBloom == true)
            {
                int combo = scoreProcessor?.Combo.Value ?? 0;
                int maxSizeCombo = Math.Clamp(settings.BloomMaxSizeComboCount, 5, 100);
                float maxSize = Math.Clamp(settings.BloomMaxCursorSize, 5f, 15f);

                scale = Math.Clamp(maxSize * ((float)combo / maxSizeCombo), 1, maxSize);

                if (player?.IsBreakTime.Value == true)
                    scale = 1;
            }

            osuCursor.ModScaleAdjust.Value = scale;
        }

        private void updateBubblesJudgementVisibility(SectionGimmickSettings? settings)
        {
            if (!hasForcedBubbles)
                return;

            drawableRuleset.Playfield.DisplayJudgements.Value = settings?.ForceBubbles == true ? false : initialDisplayJudgements;
        }

        private void updateMotionEffects()
        {
            if (!hasForcedMotionEffects)
                return;

            if (drawableRuleset.Playfield.Cursor == null)
                return;

            Vector2 cursorPos = drawableRuleset.Playfield.Cursor.ActiveCursor.DrawPosition;
            double time = drawableRuleset.Playfield.Time.Current;

            foreach (var entry in drawableRuleset.Playfield.HitObjectContainer.AliveEntries)
            {
                DrawableHitObject drawable = entry.Value;
                SectionGimmickSettings? settings = SectionModApplicator.ResolveSettingsForHitObject(beatmap, drawable.HitObject);
                if (settings == null)
                    continue;

                if (settings.ForceDepth && !selectedDepth)
                {
                    applyDepth(time, drawable, Math.Clamp(settings.DepthMaxDepth, 50f, 200f));
                    continue;
                }

                if (settings.ForceMagnetised && !selectedMagnetised)
                {
                    applyMagnetised(drawable, cursorPos, Math.Clamp(settings.MagnetisedAttractionStrength, 0.05f, 1f));
                    continue;
                }

                if (settings.ForceRepel && !selectedRepel)
                    applyRepel(drawable, cursorPos, Math.Clamp(settings.RepelRepulsionStrength, 0.05f, 1f));
            }
        }

        private void applyMagnetised(DrawableHitObject drawable, Vector2 cursorPos, float attractionStrength)
        {
            switch (drawable)
            {
                case DrawableHitCircle circle:
                    easeTo(drawableRuleset.Playfield, circle, cursorPos, attractionStrength);
                    break;

                case DrawableSlider slider:
                    if (!slider.HeadCircle.Result.HasResult)
                        easeTo(drawableRuleset.Playfield, slider, cursorPos, attractionStrength);
                    else
                        easeTo(drawableRuleset.Playfield, slider, cursorPos - slider.Ball.DrawPosition, attractionStrength);
                    break;
            }
        }

        private void applyRepel(DrawableHitObject drawable, Vector2 cursorPos, float repulsionStrength)
        {
            Vector2 destination = Vector2.Clamp(2 * drawable.Position - cursorPos, Vector2.Zero, OsuPlayfield.BASE_SIZE);

            if (drawable.HitObject is Slider sliderHitObject)
            {
                var possibleMovementBounds = OsuHitObjectGenerationUtils.CalculatePossibleMovementBounds(sliderHitObject);
                destination = Vector2.Clamp(
                    destination,
                    new Vector2(possibleMovementBounds.Left, possibleMovementBounds.Top),
                    new Vector2(possibleMovementBounds.Right, possibleMovementBounds.Bottom));
            }

            switch (drawable)
            {
                case DrawableHitCircle circle:
                    easeToRepel(drawableRuleset.Playfield, circle, destination, cursorPos, repulsionStrength);
                    break;

                case DrawableSlider slider:
                    if (!slider.HeadCircle.Result.HasResult)
                        easeToRepel(drawableRuleset.Playfield, slider, destination, cursorPos, repulsionStrength);
                    else
                        easeToRepel(drawableRuleset.Playfield, slider, destination - slider.Ball.DrawPosition, cursorPos, repulsionStrength);
                    break;
            }
        }

        private static void easeTo(Playfield playfield, DrawableHitObject hitObject, Vector2 destination, float attractionStrength)
        {
            double dampLength = Interpolation.Lerp(3000, 40, attractionStrength);

            float x = (float)Interpolation.DampContinuously(hitObject.X, destination.X, dampLength, playfield.Clock.ElapsedFrameTime);
            float y = (float)Interpolation.DampContinuously(hitObject.Y, destination.Y, dampLength, playfield.Clock.ElapsedFrameTime);

            hitObject.Position = new Vector2(x, y);
        }

        private static void easeToRepel(Playfield playfield, DrawableHitObject hitObject, Vector2 destination, Vector2 cursorPos, float repulsionStrength)
        {
            double dampLength = Vector2.Distance(hitObject.Position, cursorPos) / (0.04 * repulsionStrength + 0.04);

            float x = (float)Interpolation.DampContinuously(hitObject.X, destination.X, dampLength, playfield.Clock.ElapsedFrameTime);
            float y = (float)Interpolation.DampContinuously(hitObject.Y, destination.Y, dampLength, playfield.Clock.ElapsedFrameTime);

            hitObject.Position = new Vector2(x, y);
        }

        private static void applyDepth(double time, DrawableHitObject drawable, float maxDepth)
        {
            switch (drawable)
            {
                case DrawableHitCircle circle:
                    processDepthHitObject(time, circle, maxDepth);
                    break;

                case DrawableSlider slider:
                    processDepthSlider(time, slider, maxDepth);
                    break;
            }
        }

        private static void processDepthHitObject(double time, DrawableOsuHitObject drawable, float maxDepth)
        {
            var hitObject = drawable.HitObject;

            double speed = maxDepth / hitObject.TimePreempt;
            double appearTime = hitObject.StartTime - hitObject.TimePreempt;
            float z = maxDepth - (float)((Math.Max(time, appearTime) - appearTime) * speed);

            float scale = depthScaleFor(z);
            drawable.Position = depthToPlayfieldPosition(scale, hitObject.StackedPosition);
            drawable.Scale = new Vector2(scale);
        }

        private static void processDepthSlider(double time, DrawableSlider drawableSlider, float maxDepth)
        {
            var hitObject = drawableSlider.HitObject;

            double baseSpeed = maxDepth / hitObject.TimePreempt;
            double appearTime = hitObject.StartTime - hitObject.TimePreempt;

            float sliderMinDepth = depthForScale(1.5f);
            float zEnd = maxDepth - (float)((Math.Max(hitObject.StartTime + hitObject.Duration, appearTime) - appearTime) * baseSpeed);

            if (zEnd > sliderMinDepth)
            {
                processDepthHitObject(time, drawableSlider, maxDepth);
                return;
            }

            double offsetAfterStartTime = hitObject.Duration + 500;
            double slowSpeed = Math.Min(-sliderMinDepth / offsetAfterStartTime, baseSpeed);

            double decelerationTime = hitObject.TimePreempt * 0.2;
            float decelerationDistance = (float)(decelerationTime * (baseSpeed + slowSpeed) * 0.5);

            float z;

            if (time < hitObject.StartTime - decelerationTime)
            {
                float fullDistance = decelerationDistance + (float)(baseSpeed * (hitObject.TimePreempt - decelerationTime));
                z = fullDistance - (float)((Math.Max(time, appearTime) - appearTime) * baseSpeed);
            }
            else if (time < hitObject.StartTime)
            {
                double timeOffset = time - (hitObject.StartTime - decelerationTime);
                double deceleration = (slowSpeed - baseSpeed) / decelerationTime;
                z = decelerationDistance - (float)(baseSpeed * timeOffset + deceleration * timeOffset * timeOffset * 0.5);
            }
            else
            {
                double endTime = hitObject.StartTime + offsetAfterStartTime;
                z = -(float)((Math.Min(time, endTime) - hitObject.StartTime) * slowSpeed);
            }

            float scale = depthScaleFor(z);
            drawableSlider.Position = depthToPlayfieldPosition(scale, hitObject.StackedPosition);
            drawableSlider.Scale = new Vector2(scale);
        }

        private static float depthScaleFor(float depth) => -depth_camera_position.Z / Math.Max(1f, depth - depth_camera_position.Z);

        private static float depthForScale(float scale) => -depth_camera_position.Z / scale + depth_camera_position.Z;

        private static Vector2 depthToPlayfieldPosition(float scale, Vector2 positionAtZeroDepth)
            => (positionAtZeroDepth - depth_camera_position.Xy) * scale + depth_camera_position.Xy;

        private static void applyModToDrawable(Mod mod, DrawableHitObject drawable)
        {
            if (mod is IApplicableToDrawableHitObject applicable)
                applicable.ApplyToDrawableHitObject(drawable);
        }

        internal SectionGimmickSettings? resolveSettingsAtTime(double time)
            => gimmicks.FindSectionAt(time)?.Settings;

        private bool hasAnyForced(Func<SectionGimmickSettings, bool> predicate)
            => gimmicks.Sections.Any(s => predicate(s.Settings));

        private bool hasSelectedMod<TMod>()
            where TMod : Mod
            => selectedMods.Any(m => m is TMod);

        /// <summary>
        /// Section-scoped copy of OsuModBubbles' private bubble drawable (the original is a private
        /// nested class and cannot be used from the ruleset).
        /// </summary>
        private partial class BubbleDrawable : osu.Framework.Graphics.Pooling.PoolableDrawable
        {
            public Vector2 InitialSize { get; set; }

            public float MaxSize { get; set; }

            public double FadeTime { get; set; }

            public bool WasHit { get; set; }

            public Color4 AccentColour { get; set; }

            private readonly Box colourBox;
            private readonly CircularContainer content;

            public BubbleDrawable()
            {
                Origin = Anchor.Centre;
                InternalChild = content = new CircularContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Both,
                    MaskingSmoothness = 2,
                    BorderThickness = 0,
                    BorderColour = Colour4.White,
                    Masking = true,
                    EdgeEffect = new EdgeEffectParameters
                    {
                        Type = EdgeEffectType.Shadow,
                        Radius = 3,
                        Colour = Colour4.Black.Opacity(0.05f),
                    },
                    Child = colourBox = new Box { RelativeSizeAxes = Axes.Both, }
                };
            }

            protected override void PrepareForUse()
            {
                Colour = WasHit ? Colour4.White : Colour4.Black;
                Scale = new Vector2(1);
                Size = InitialSize;

                Color4 colourDarker = AccentColour.Darken(0.1f);

                double duration = 1700 + Math.Pow(FadeTime, 1.07f);

                this.FadeTo(1)
                    .ScaleTo(MaxSize, duration * 0.8f)
                    .Then()
                    .ScaleTo(MaxSize * 1.5f, duration * 0.2f, Easing.OutQuint)
                    .FadeOut(duration * 0.2f, Easing.OutCirc).Expire();

                if (!WasHit)
                    return;

                content.BorderThickness = InitialSize.X / 3.5f;
                content.BorderColour = Colour4.White;

                colourBox.FadeColour(colourDarker);

                content.TransformTo(nameof(content.BorderColour), colourDarker, duration * 0.3f, Easing.OutQuint);
                content.TransformTo(nameof(content.BorderThickness), 2f, duration * 0.3f, Easing.OutQuint)
                       .Then()
                       .TransformTo(nameof(content.BorderThickness), 0f);
            }
        }
    }
}
