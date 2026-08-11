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

        public MosuDrawableHitCircle(HitCircle h, bool hidden, bool noApproachCircle)
            : base(h)
        {
            this.hidden = hidden;
            this.noApproachCircle = noApproachCircle;
        }

        protected override void UpdateInitialTransforms()
        {
            base.UpdateInitialTransforms();

            if (noApproachCircle)
                UI.SectionGimmickHiddenVisuals.ApplyHiddenState(this, onlyFadeApproachCircles: true);

            if (hidden)
                UI.SectionGimmickHiddenVisuals.ApplyHiddenState(this);
        }
    }

    public partial class MosuDrawableSliderHead : MosuDrawableHitCircle
    {
        public MosuDrawableSliderHead(SliderHeadCircle h, bool hidden, bool noApproachCircle)
            : base(h, hidden, noApproachCircle)
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

        public MosuDrawableSliderRepeat(SliderRepeat sliderRepeat, bool hidden)
            : base(sliderRepeat)
        {
            this.hidden = hidden;
        }

        protected override void UpdateInitialTransforms()
        {
            base.UpdateInitialTransforms();

            if (hidden)
                UI.SectionGimmickHiddenVisuals.ApplyHiddenState(this);
        }
    }

    public partial class MosuDrawableSliderTail : DrawableSliderTail
    {
        private readonly bool hidden;

        public MosuDrawableSliderTail(SliderTailCircle tailCircle, bool hidden)
            : base(tailCircle)
        {
            this.hidden = hidden;
        }

        protected override void UpdateInitialTransforms()
        {
            base.UpdateInitialTransforms();

            if (hidden)
                UI.SectionGimmickHiddenVisuals.ApplyHiddenState(this);
        }
    }

    public partial class MosuDrawableSlider : DrawableSlider
    {
        private readonly bool hidden;
        private readonly bool noApproachCircle;

        public MosuDrawableSlider(Slider h, bool hidden, bool noApproachCircle)
            : base(h)
        {
            this.hidden = hidden;
            this.noApproachCircle = noApproachCircle;
        }

        protected override DrawableHitObject CreateNestedHitObject(HitObject hitObject)
        {
            switch (hitObject)
            {
                case SliderTailCircle tail:
                    return new MosuDrawableSliderTail(tail, hidden);

                case SliderHeadCircle head:
                    return new MosuDrawableSliderHead(head, hidden, noApproachCircle);

                case SliderTick tick:
                    return new MosuDrawableSliderTick(tick, hidden);

                case SliderRepeat repeat:
                    return new MosuDrawableSliderRepeat(repeat, hidden);
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
        }
    }

    public partial class MosuDrawableSpinner : DrawableSpinner
    {
        private readonly bool hidden;
        private readonly bool noApproachCircle;

        public MosuDrawableSpinner(Spinner h, bool hidden, bool noApproachCircle)
            : base(h)
        {
            this.hidden = hidden;
            this.noApproachCircle = noApproachCircle;
        }

        protected override void UpdateInitialTransforms()
        {
            base.UpdateInitialTransforms();

            if (noApproachCircle)
                UI.SectionGimmickHiddenVisuals.ApplyHiddenState(this, onlyFadeApproachCircles: true);

            if (hidden)
                UI.SectionGimmickHiddenVisuals.ApplyHiddenState(this);
        }
    }
}
