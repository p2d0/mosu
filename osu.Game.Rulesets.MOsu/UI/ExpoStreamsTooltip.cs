using System;
using osu.Game.Rulesets.MOsu.Mods;

namespace osu.Game.Rulesets.MOsu.UI
{
    public partial class ExpoStreamsTooltip : ExpoTooltip
    {
        public ExpoStreamsTooltip() { }
        public ExpoStreamsTooltip(string description) : base(description) { }

        protected override float[] UnevenOffsets => new[] { 10f, 20f, 30f, 160f, 200f, 280f, 320f };
        protected override float[] EvenOffsetsLow => new[] { 10f, 60f, 110f, 160f, 205f, 280f, 320f };
        protected override float[] EvenOffsetsHigh => new[] { 00f, 50f, 100f, 150f, 200f, 290f, 350f };
        protected override string[] CircleLabels => new[] { "¼", "¼", "¼", "¼", "¼", "½", "½", "½" };
        public override void SetState(bool even, int divisor = 2)
        {
            float[] target = UnevenOffsets;
            if (even && divisor < 4)
                target = divisor >= 2 ? EvenOffsetsLow : EvenOffsetsHigh;
            AnimateTo(target);
        }
    }
}
