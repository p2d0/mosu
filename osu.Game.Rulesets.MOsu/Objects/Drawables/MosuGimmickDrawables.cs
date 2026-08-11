// Drawables applying per-object section gimmick visuals (forced hidden / no-approach-circle)
// that the stock osu! drawables don't know about.
// Visuals ported from delta fork (DrawableOsuHitObject + OsuModHidden).

using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;

namespace osu.Game.Rulesets.MOsu.Objects.Drawables
{
    public partial class MosuDrawableHitCircle : DrawableHitCircle
    {
        private readonly bool hidden;
        private readonly bool noApproachCircle;
        private readonly bool traceable;

        public MosuDrawableHitCircle(HitCircle h, bool hidden, bool noApproachCircle, bool traceable)
            : base(h)
        {
            this.hidden = hidden;
            this.noApproachCircle = noApproachCircle;
            this.traceable = traceable;
        }

        protected override void UpdateInitialTransforms()
        {
            base.UpdateInitialTransforms();

            if (noApproachCircle)
                UI.SectionGimmickHiddenVisuals.ApplyHiddenState(this, onlyFadeApproachCircles: true);

            if (hidden)
                UI.SectionGimmickHiddenVisuals.ApplyHiddenState(this);

            if (traceable)
                UI.SectionGimmickTraceableVisuals.ApplyTraceableState(this);
        }
    }

    public partial class MosuDrawableSliderHead : MosuDrawableHitCircle
    {
        public MosuDrawableSliderHead(SliderHeadCircle h, bool hidden, bool noApproachCircle, bool traceable)
            : base(h, hidden, noApproachCircle, traceable)
        {
        }
    }

    public partial class MosuDrawableSliderTick : DrawableSliderTick
    {
        private readonly bool hidden;

        public MosuDrawableSliderTick(SliderTick sliderTick, bool hidden)
            : base(sliderTick)
        {
            this.hidden = hidden;
        }

        // traceable leaves ticks visible (matches osu TC mod behaviour).

        protected override void UpdateInitialTransforms()
        {
            base.UpdateInitialTransforms();

            if (hidden)
                UI.SectionGimmickHiddenVisuals.ApplyHiddenState(this);
        }
    }

    public partial class MosuDrawableSliderRepeat : DrawableSliderRepeat
    {
        private readonly bool hidden;
        private readonly bool traceable;

        public MosuDrawableSliderRepeat(SliderRepeat sliderRepeat, bool hidden, bool traceable)
            : base(sliderRepeat)
        {
            this.hidden = hidden;
            this.traceable = traceable;
        }

        protected override void UpdateInitialTransforms()
        {
            base.UpdateInitialTransforms();

            if (hidden)
                UI.SectionGimmickHiddenVisuals.ApplyHiddenState(this);

            if (traceable)
                UI.SectionGimmickTraceableVisuals.ApplyTraceableState(this);
        }
    }

    public partial class MosuDrawableSliderTail : DrawableSliderTail
    {
        private readonly bool hidden;
        private readonly bool traceable;

        public MosuDrawableSliderTail(SliderTailCircle tailCircle, bool hidden, bool traceable)
            : base(tailCircle)
        {
            this.hidden = hidden;
            this.traceable = traceable;
        }

        protected override void UpdateInitialTransforms()
        {
            base.UpdateInitialTransforms();

            if (hidden)
                UI.SectionGimmickHiddenVisuals.ApplyHiddenState(this);

            if (traceable)
                UI.SectionGimmickTraceableVisuals.ApplyTraceableState(this);
        }
    }

    public partial class MosuDrawableSlider : DrawableSlider
    {
        private readonly bool hidden;
        private readonly bool noApproachCircle;
        private readonly bool traceable;

        public MosuDrawableSlider(Slider h, bool hidden, bool noApproachCircle, bool traceable)
            : base(h)
        {
            this.hidden = hidden;
            this.noApproachCircle = noApproachCircle;
            this.traceable = traceable;
        }

        protected override DrawableHitObject CreateNestedHitObject(HitObject hitObject)
        {
            switch (hitObject)
            {
                case SliderTailCircle tail:
                    return new MosuDrawableSliderTail(tail, hidden, traceable);

                case SliderHeadCircle head:
                    return new MosuDrawableSliderHead(head, hidden, noApproachCircle, traceable);

                case SliderTick tick:
                    return new MosuDrawableSliderTick(tick, hidden);

                case SliderRepeat repeat:
                    return new MosuDrawableSliderRepeat(repeat, hidden, traceable);
            }

            return base.CreateNestedHitObject(hitObject);
        }

        protected override void UpdateInitialTransforms()
        {
            base.UpdateInitialTransforms();

            if (noApproachCircle)
                UI.SectionGimmickHiddenVisuals.ApplyHiddenState(this, onlyFadeApproachCircles: true);

            if (hidden)
                UI.SectionGimmickHiddenVisuals.ApplyHiddenState(this);

            if (traceable)
                UI.SectionGimmickTraceableVisuals.ApplyTraceableState(this);
        }
    }

    public partial class MosuDrawableSpinner : DrawableSpinner
    {
        private readonly bool hidden;
        private readonly bool noApproachCircle;
        private readonly bool traceable;

        public MosuDrawableSpinner(Spinner h, bool hidden, bool noApproachCircle, bool traceable)
            : base(h)
        {
            this.hidden = hidden;
            this.noApproachCircle = noApproachCircle;
            this.traceable = traceable;
        }

        protected override void UpdateInitialTransforms()
        {
            base.UpdateInitialTransforms();

            if (noApproachCircle)
                UI.SectionGimmickHiddenVisuals.ApplyHiddenState(this, onlyFadeApproachCircles: true);

            if (hidden)
                UI.SectionGimmickHiddenVisuals.ApplyHiddenState(this);

            // traceable leaves spinners unchanged (matches osu TC mod behaviour).
        }
    }
}
