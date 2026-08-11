// Ported from delta fork (osu.Game.Rulesets.Osu/Mods/OsuModTraceable.cs applyTraceableState),
// used to apply per-object traceable visuals to MOsu drawables
// (the NuGet OsuModTraceable keeps this logic private).

using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Rulesets.Osu.Skinning.Default;

namespace osu.Game.Rulesets.MOsu.UI
{
    public static class SectionGimmickTraceableVisuals
    {
        public static void ApplyTraceableState(DrawableHitObject drawable)
        {
            if (drawable is not DrawableOsuHitObject)
                return;

            switch (drawable)
            {
                case DrawableHitCircle circle:
                    // we only want to see the approach circle
                    applyCirclePieceState(circle, circle.CirclePiece);
                    break;

                case DrawableSliderTail sliderTail:
                    applyCirclePieceState(sliderTail);
                    break;

                case DrawableSliderRepeat sliderRepeat:
                    // show only the repeat arrow
                    applyCirclePieceState(sliderRepeat, sliderRepeat.CirclePiece);
                    break;

                case DrawableSlider slider:
                    slider.Body.OnSkinChanged += () => applySliderState(slider);
                    applySliderState(slider);
                    break;
            }
        }

        private static void applyCirclePieceState(DrawableOsuHitObject hitObject, IDrawable? hitCircle = null)
        {
            var h = hitObject.HitObject;
            using (hitObject.BeginAbsoluteSequence(h.StartTime - h.TimePreempt))
                (hitCircle ?? hitObject).Hide();
        }

        private static void applySliderState(DrawableSlider slider)
        {
            ((PlaySliderBody)slider.Body.Drawable).AccentColour = slider.AccentColour.Value.Opacity(0);
            ((PlaySliderBody)slider.Body.Drawable).BorderColour = slider.AccentColour.Value;
        }
    }
}
