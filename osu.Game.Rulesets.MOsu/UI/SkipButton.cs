// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

#nullable disable

using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Screens.Ranking;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.MOsu.UI
{
    public partial class SkipButton : OsuClickableContainer
    {
        private Color4 colourNormal;
        private Color4 colourHover;
        private Box box;
        private FillFlowContainer flow;
        private Box background;
        private AspectContainer aspect;

        private Sample sampleConfirm;

        public SkipButton()
        {
            RelativeSizeAxes = Axes.Both;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours, AudioManager audio)
        {
            colourNormal = colours.Yellow;
            colourHover = colours.YellowDark;

            sampleConfirm = audio.Samples.Get(@"UI/submit-select");

            Children = new Drawable[]
            {
                background = new Box
                {
                    Alpha = 0.2f,
                    Colour = Color4.Black,
                    RelativeSizeAxes = Axes.Both,
                },
                aspect = new AspectContainer
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    RelativeSizeAxes = Axes.Y,
                    Height = 0.6f,
                    Masking = true,
                    CornerRadius = 15,
                    Children = new Drawable[]
                    {
                        box = new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = colourNormal,
                        },
                        flow = new FillFlowContainer
                        {
                            Anchor = Anchor.TopCentre,
                            RelativePositionAxes = Axes.Y,
                            Y = 0.4f,
                            AutoSizeAxes = Axes.Both,
                            Origin = Anchor.Centre,
                            Direction = FillDirection.Horizontal,
                            Children = new[]
                            {
                                new SpriteIcon { Size = new Vector2(15), Shadow = true, Icon = FontAwesome.Solid.ChevronRight },
                                new SpriteIcon { Size = new Vector2(15), Shadow = true, Icon = FontAwesome.Solid.ChevronRight },
                                new SpriteIcon { Size = new Vector2(15), Shadow = true, Icon = FontAwesome.Solid.ChevronRight },
                            }
                        },
                        new OsuSpriteText
                        {
                            Anchor = Anchor.TopCentre,
                            RelativePositionAxes = Axes.Y,
                            Y = 0.7f,
                            Font = OsuFont.GetFont(weight: FontWeight.Bold, size: 12),
                            Origin = Anchor.Centre,
                            Text = @"SKIP",
                        },
                    }
                }
            };
        }

        protected override bool OnHover(HoverEvent e)
        {
            flow.TransformSpacingTo(new Vector2(5), 500, Easing.OutQuint);
            box.FadeColour(colourHover, 500, Easing.OutQuint);
            background.FadeTo(0.4f, 500, Easing.OutQuint);
            return true;
        }

        protected override void OnHoverLost(HoverLostEvent e)
        {
            flow.TransformSpacingTo(new Vector2(0), 500, Easing.OutQuint);
            box.FadeColour(colourNormal, 500, Easing.OutQuint);
            background.FadeTo(0.2f, 500, Easing.OutQuint);
            base.OnHoverLost(e);
        }

        protected override bool OnMouseDown(MouseDownEvent e)
        {
            aspect.ScaleTo(0.75f, 2000, Easing.OutQuint);
            return base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseUpEvent e)
        {
            aspect.ScaleTo(1, 1000, Easing.OutElastic);
            base.OnMouseUp(e);
        }

        protected override bool OnClick(ClickEvent e)
        {
            if (!Enabled.Value)
                return false;

            sampleConfirm.Play();

            box.FlashColour(Color4.White, 500, Easing.OutQuint);
            aspect.ScaleTo(1.2f, 2000, Easing.OutQuint);

            return base.OnClick(e);
        }
    }
}
