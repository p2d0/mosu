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
    // --- Setting base ---

    public abstract partial class ExpoSetting<TTooltip> : SettingsCheckbox, IHasCustomTooltip
        where TTooltip : ExpoTooltip, new()
    {
        private readonly TTooltip tooltip;
        private OsuModRandomV2? mod;

        protected ExpoSetting(string description)
        {
            tooltip = new TTooltip { Description = description };
        }

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

    public partial class ExpoJumpsSetting : ExpoSetting<ExpoJumpsTooltip>
    {
        public ExpoJumpsSetting()
            : base("Larger jump spacing receives diminishing distance increases") { }
    }

    public partial class ExpoStreamsSetting : ExpoSetting<ExpoStreamsTooltip>
    {
        public ExpoStreamsSetting()
            : base("Larger stream spacing receives diminishing distance increases") { }
    }

    // --- Tooltip base ---

    public abstract partial class ExpoTooltip : CompositeDrawable, ITooltip
    {
        private const float circle_size = 28f;
        private const double transition_duration = 100;
        private const float padding = 10f;

        private Container[] circleContainers = Array.Empty<Container>();
        private float maxWidth;
        private string? description;

        public string? Description
        {
            set => description = value;
        }

        protected abstract float[] UnevenOffsets { get; }
        protected abstract float[] EvenOffsetsLow { get; }
        protected abstract float[] EvenOffsetsHigh { get; }
        protected abstract string[] CircleLabels { get; }
        public ExpoTooltip() { }
        public ExpoTooltip(string description) { Description = description; }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            CornerRadius = 5;
            Masking = true;

            maxWidth = Math.Max(UnevenOffsets[^1], Math.Max(EvenOffsetsLow[^1], EvenOffsetsHigh[^1])) + circle_size + padding * 2;

            Width = maxWidth + 50;
            Height = circle_size + padding * 2 + 12;

            var fillFlow = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.Both,
                Direction = FillDirection.Vertical,
                Spacing = new Vector2(10),
            };

            AddInternal(new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = colours.Gray3,
                Alpha = 0.9f,
            });
            AddInternal(fillFlow);

            if (description != null)
            {
                fillFlow.Add(new TextFlowContainer(f =>
                {
                    f.Font = OsuFont.GetFont(weight: FontWeight.Regular);
                })
                {
                    Text = description,
                    TextAnchor = Anchor.TopCentre,
                    Padding = new MarginPadding { Top = 5 },
                    RelativeSizeAxes = Axes.X,
                });
            }

            var circleContainer = new Container { RelativeSizeAxes = Axes.Both };
            fillFlow.Add(circleContainer);

            var offsets = UnevenOffsets;
            circleContainers = new Container[offsets.Length];
            for (int i = offsets.Length - 1; i >= 0; i--)
            {
                var container = new Container
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                    X = offsets[i] - (maxWidth - circle_size) / 2,
                    Child = new StaticCirclePiece(CircleLabels[i])
                    {
                        Size = new Vector2(circle_size),
                    },
                };
                circleContainers[i] = container;
                circleContainer.Add(container);
            }
        }

        public abstract void SetState(bool even, int divisor = 2);

        protected void AnimateTo(float[] target)
        {
            for (int i = 0; i < UnevenOffsets.Length; i++)
                circleContainers[i].MoveToX(target[i] - (maxWidth - circle_size) / 2, transition_duration, Easing.OutQuint);
        }

        public void SetContent(object content) { }
        public void Move(Vector2 pos) => Position = pos;
    }

    public partial class ExpoJumpsTooltip : ExpoTooltip
    {
        public ExpoJumpsTooltip() { }
        public ExpoJumpsTooltip(string description) : base(description) { }

        protected override float[] UnevenOffsets => new[] { 10f, 20f, 30f, 90f, 100f, 110f, 200f, 280f };
        protected override float[] EvenOffsetsLow => new[] { 10f, 20f, 30f, 100f, 160f, 220f, 280f, 365f };
        protected override float[] EvenOffsetsHigh => new[] { 00f, 50f, 100f, 150f, 200f, 250f, 300f, 365f };
        protected override string[] CircleLabels => new[] { "¼", "¼", "¼", "½", "½", "½", "½", "½", "½" };
        public override void SetState(bool even, int divisor = 2)
        {
            float[] target = UnevenOffsets;
            if (even && divisor >= 2)
                target = divisor >= 4 ? EvenOffsetsHigh : EvenOffsetsLow;
            AnimateTo(target);
        }
    }

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

    // --- Shared UI pieces ---

    public partial class StaticCirclePiece : Container
    {
        private Texture? discTexture;
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
            discTexture = textures.Get(@"Gameplay/osu/disc");
            InternalChildren = new Drawable[]
            {
                new CircularContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Children = new Drawable[]
                    {
                        new Box { RelativeSizeAxes = Axes.Both, Colour = Color4.White },
                        new Sprite
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                            Texture = discTexture,
                            FillMode = FillMode.Fit,
                        },
                    },
                },
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
