// Ported from delta fork (osu.Game.Rulesets.Osu/Mods/OsuModHidden.cs ApplyHiddenState),
// used to apply per-object hidden / no-approach-circle visuals to MOsu drawables
// (the NuGet OsuModHidden keeps this logic private).

using System;
using osu.Framework.Graphics;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Rulesets.Osu.Skinning;
using osu.Game.Rulesets.Osu.UI;

namespace osu.Game.Rulesets.MOsu.Delta.UI
{
    public static class SectionGimmickHiddenVisuals
    {
        public const double FADE_OUT_DURATION_MULTIPLIER = 0.3;

        public static void ApplyHiddenState(DrawableHitObject drawableObject, bool increaseVisibility = false, bool onlyFadeApproachCircles = false)
        {
            if (drawableObject is not DrawableOsuHitObject drawableOsuObject)
                return;

            OsuHitObject hitObject = drawableOsuObject.HitObject;

            (double fadeStartTime, double fadeDuration) = getFadeOutParameters(drawableOsuObject);

            // process approach circle hiding first (to allow for early return below).
            if (!increaseVisibility)
            {
                if (drawableObject is DrawableHitCircle circle)
                {
                    using (circle.BeginAbsoluteSequence(hitObject.StartTime - hitObject.TimePreempt))
                        circle.ApproachCircle.Hide();
                }
                else if (drawableObject is DrawableSpinner spinner)
                {
                    spinner.Body.OnSkinChanged += () => hideSpinnerApproachCircle(spinner);
                    hideSpinnerApproachCircle(spinner);
                }
            }

            if (onlyFadeApproachCircles)
                return;

            switch (drawableObject)
            {
                case DrawableSliderTail:
                    using (drawableObject.BeginAbsoluteSequence(fadeStartTime))
                        drawableObject.FadeOut(fadeDuration);

                    break;

                case DrawableSliderRepeat sliderRepeat:
                    using (drawableObject.BeginAbsoluteSequence(fadeStartTime))
                        // only apply to circle piece – reverse arrow is not affected by hidden.
                        sliderRepeat.CirclePiece.FadeOut(fadeDuration);

                    using (drawableObject.BeginAbsoluteSequence(drawableObject.HitStateUpdateTime))
                        sliderRepeat.FadeOut();

                    break;

                case DrawableHitCircle circle:
                    Drawable fadeTarget = circle;

                    if (increaseVisibility)
                    {
                        // only fade the circle piece (not the approach circle) for the increased visibility object.
                        fadeTarget = circle.CirclePiece;
                    }

                    using (drawableObject.BeginAbsoluteSequence(fadeStartTime))
                        fadeTarget.FadeOut(fadeDuration);
                    break;

                case DrawableSlider slider:
                    using (slider.BeginAbsoluteSequence(fadeStartTime))
                        slider.Body.FadeOut(fadeDuration, Easing.Out);

                    break;

                case DrawableSliderTick sliderTick:
                    using (sliderTick.BeginAbsoluteSequence(fadeStartTime))
                        sliderTick.FadeOut(fadeDuration);

                    break;

                case DrawableSpinner spinner:
                    using (spinner.BeginAbsoluteSequence(fadeStartTime))
                        spinner.FadeOut(fadeDuration);

                    break;
            }
        }

        private static (double fadeStartTime, double fadeDuration) getFadeOutParameters(DrawableOsuHitObject drawableObject)
        {
            switch (drawableObject)
            {
                case DrawableSliderTail tail:
                    // Use the same fade sequence as the slider head.
                    return getParameters(tail.Slider?.HeadCircle ?? drawableObject.HitObject);

                case DrawableSliderRepeat repeat:
                    // Use the same fade sequence as the slider head.
                    return getParameters(repeat.Slider?.HeadCircle ?? drawableObject.HitObject);

                default:
                    return getParameters(drawableObject.HitObject);
            }

            static (double fadeStartTime, double fadeDuration) getParameters(OsuHitObject hitObject)
            {
                double fadeOutStartTime = hitObject.StartTime - hitObject.TimePreempt + hitObject.TimeFadeIn;
                double fadeOutDuration = hitObject.TimePreempt * FADE_OUT_DURATION_MULTIPLIER;

                // new duration from completed fade in to end (before fading out)
                double longFadeDuration = hitObject.GetEndTime() - fadeOutStartTime;

                switch (hitObject)
                {
                    case Slider:
                        return (fadeOutStartTime, longFadeDuration);

                    case SliderTick:
                        double tickFadeOutDuration = Math.Min(hitObject.TimePreempt - DrawableSliderTick.ANIM_DURATION, 1000);
                        return (hitObject.StartTime - tickFadeOutDuration, tickFadeOutDuration);

                    default:
                        return (fadeOutStartTime, fadeOutDuration);
                }
            }
        }

        private static void hideSpinnerApproachCircle(DrawableSpinner spinner)
        {
            var approachCircle = (spinner.Body.Drawable as IHasApproachCircle)?.ApproachCircle;
            if (approachCircle == null)
                return;

            using (spinner.BeginAbsoluteSequence(spinner.HitObject.StartTime - spinner.HitObject.TimePreempt))
                approachCircle.Hide();
        }
    }
}
