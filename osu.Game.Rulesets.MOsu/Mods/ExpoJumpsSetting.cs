using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays.Settings;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.MOsu.Mods
{
    public partial class ExpoJumpsSetting : SettingsCheckbox, IHasCustomTooltip
    {
        private readonly ExpoJumpsTooltip tooltip = new();
        private OsuModRandomV2? mod;

        ITooltip IHasCustomTooltip.GetCustomTooltip() => tooltip;
        object? IHasCustomTooltip.TooltipContent => true;

        protected override void LoadComplete()
        {
            base.LoadComplete();
            mod = SettingSourceObject as OsuModRandomV2;
            Current.BindValueChanged(e =>
            {
                if (tooltip.IsLoaded)
                    tooltip.SetState(e.NewValue, mod?.Divisor.Value ?? 2);
            }, true);
            mod?.Divisor.BindValueChanged(_ =>
            {
                if (tooltip.IsLoaded)
                    tooltip.SetState(Current.Value, mod.Divisor.Value);
            });
        }
    }

    public partial class ExpoJumpsTooltip : CompositeDrawable, ITooltip
    {
        private const float circle_size = 28f;
        private const double transition_duration = 100;
        private const float padding = 10f;

        private static readonly float[] uneven_offsets =
        {
            10f, 20f, 30f,  90f, 100f, 110f,  280f
        };

        private static readonly float[] even_offsets_low =
        {
            10f, 20f, 30f,  100f, 160f, 220f,  280f
        };

        private static readonly float[] even_offsets_high =
        {
            00f, 50f, 100f,  150f, 200f, 250f,  300f
        };

        private Container[] circleContainers;
        private float maxWidth;
        private float minHeight;

        public ExpoJumpsTooltip()
        {
            CornerRadius = 10;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            maxWidth = Math.Max(uneven_offsets[^1], Math.Max(even_offsets_low[^1], even_offsets_high[^1])) + circle_size + padding * 2;
            minHeight = circle_size + padding * 2;

            Width = maxWidth;
            Height = minHeight;

            AddInternal(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = colours.Gray1,
            });

            circleContainers = new Container[uneven_offsets.Length];
            for (int i = uneven_offsets.Length - 1; i >= 0; i--)
            {
                var container = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    X = uneven_offsets[i] - (maxWidth - circle_size) / 2,
                    Child = new StaticCirclePiece(i > 2 ? "½" : "¼")
                    {
                        Size = new Vector2(circle_size),
                    },
                };

                circleContainers[i] = container;
                AddInternal(container);
            }
        }

        public void SetState(bool even, int divisor = 4)
        {
            float[] target = uneven_offsets;
            if (even)
                target = divisor >= 4 ? even_offsets_high : even_offsets_low;
            for (int i = 0; i < uneven_offsets.Length; i++)
                circleContainers[i].MoveToX(target[i] - (maxWidth - circle_size) / 2, transition_duration, Easing.OutQuint);
        }

        public void SetContent(object content) { }
        public void Move(Vector2 pos) => Position = pos;
    }

    public partial class StaticCirclePiece : Container
    {
        private readonly Texture? discTexture;
        private string number;
        public StaticCirclePiece(string number)
        {
            this.number = number;
            Anchor = Anchor.Centre;
            Origin = Anchor.Centre;
        }

        [BackgroundDependencyLoader]
        private void load(TextureStore textures)
        {
            var discTexture = textures.Get(@"Gameplay/osu/disc");
            InternalChildren = new Drawable[]
            {
                // Main circle body (masked disc)
                new CircularContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            RelativeSizeAxes = Axes.Both,
                            Colour = Color4.White,
                        },
                        new Sprite
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Texture = discTexture,
                            FillMode = FillMode.Fit,
                        },
                    },
                },

                // Outer ring
                new CircularContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    BorderThickness = 3f,
                    BorderColour = Color4.White,
                    Children = new Drawable[]
                    {
                        new Box
                        {
                            AlwaysPresent = true,
                            Alpha = 0,
                            RelativeSizeAxes = Axes.Both,
                        },
                    },
                },

                // Number
                new OsuSpriteText
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    Text = number,
                    Font = OsuFont.Numeric.With(size: 16),
                    UseFullGlyphHeight = false,
                },
            };
        }
    }

    /// <summary>
    /// Circular masked container. Mirrors osu.Framework.Graphics.Containers.CircularContainer.
    /// </summary>
    public partial class CircularContainer : Container
    {
        public CircularContainer()
        {
            Masking = true;
            CornerExponent = 2;
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();
            CornerRadius = MathF.Min(DrawWidth, DrawHeight) / 2;
        }
    }
}
