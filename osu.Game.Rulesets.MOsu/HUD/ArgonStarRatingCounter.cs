// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Bindables;
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Game.Localisation.SkinComponents;
using osu.Game.Resources.Localisation.Web;
using osu.Game.Screens.Play.HUD;
using osu.Framework.Allocation;

namespace osu.Game.Rulesets.Osu.HUD
{
    [Cached]
    public partial class ArgonStarRatingCounter : StarRatingCounter
    {
        private ArgonCounterTextComponent text = null!;

        protected override double RollingDuration => 250;

        private const float alpha_when_invalid = 0.3f;

        public BindableFloat WireframeOpacity { get; } = new BindableFloat(0.25f)
        {
            Precision = 0.01f,
            MinValue = 0,
            MaxValue = 1,
        };

        public Bindable<bool> ShowLabel { get; } = new BindableBool(true);

        public override bool IsValid
        {
            get => base.IsValid;
            set
            {
                if (value == IsValid)
                    return;

                base.IsValid = value;
                text.FadeTo(value ? 1 : alpha_when_invalid, 1000, Easing.OutQuint);
            }
        }

        public override double DisplayedCount
        {
            get => base.DisplayedCount;
            set
            {
                base.DisplayedCount = value;
                updateWireframe();
            }
        }

        private void updateWireframe()
        {
            int digitsRequired = Math.Max(3, getDigitsRequiredForDisplayCount());

            if (digitsRequired != text.WireframeTemplate.Length)
                text.WireframeTemplate = new string('#', digitsRequired);
        }

        private int getDigitsRequiredForDisplayCount()
        {
            int digitsRequired = 1;
            long c = (long)Math.Abs(DisplayedCount);
            while ((c /= 10) > 0)
                digitsRequired++;
            digitsRequired += 2; // decimal portion ".00"
            return digitsRequired;
        }

        protected override IHasText CreateText() => text = new ArgonCounterTextComponent(Anchor.TopRight, BeatmapsetsStrings.ShowStatsStars.ToUpper())
        {
            WireframeOpacity = { BindTarget = WireframeOpacity },
            ShowLabel = { BindTarget = ShowLabel },
        };
    }
}
