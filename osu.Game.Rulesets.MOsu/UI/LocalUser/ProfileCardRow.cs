using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Rulesets.MOsu.Models;
using osuTK;

namespace osu.Game.Rulesets.MOsu.UI.LocalUser
{
    public partial class ProfileCardRow : CompositeDrawable
    {
        private readonly Bindable<string> activeProfile;
        private readonly LocalUserManager localUserManager;

        private FillFlowContainer cardsContainer = null!;

        /// <summary>
        /// Fired when the "+" button is clicked. Handler should show a dialog to create a new profile.
        /// </summary>
        public Action<string> RequestAddProfile { get; set; } = null!;

        /// <summary>
        /// Fired when a profile card is right-clicked. Handler should show a delete confirmation.
        /// </summary>
        public Action<string> RequestDeleteProfile { get; set; } = null!;

        public ProfileCardRow(Bindable<string> activeProfile, LocalUserManager localUserManager)
        {
            this.activeProfile = activeProfile;
            this.localUserManager = localUserManager;

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            InternalChild = cardsContainer = new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Direction = FillDirection.Horizontal,
                Spacing = new Vector2(12),
                Padding = new MarginPadding { Bottom = 8 },
            };
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colours)
        {
            foreach (var profile in localUserManager.GetProfiles())
                addCard(profile, colours);

            cardsContainer.Add(new AddProfileButton
            {
                Height = 60,
                Width = 60,
                Action = () => RequestAddProfile?.Invoke(""),
            });
        }

        private void addCard(LocalProfile profile, OverlayColourProvider colours)
        {
            var card = new ProfileCard(profile, activeProfile, localUserManager, colours, RequestDeleteProfile);
            cardsContainer.Insert(cardsContainer.Count - 1, card);
        }

        public void Refresh()
        {
            cardsContainer.Clear();
            var colours = new OverlayColourProvider(OverlayColourScheme.Pink);
            foreach (var profile in localUserManager.GetProfiles())
                addCard(profile, colours);
            cardsContainer.Add(new AddProfileButton
            {
                Height = 60,
                Width = 60,
                Action = () => RequestAddProfile?.Invoke(""),
            });
        }

        private partial class AddProfileButton : CompositeDrawable
        {
            private readonly Box background;
            private readonly OsuSpriteText plusText;
            private OverlayColourProvider colours = null!;

            public AddProfileButton()
            {
                CornerRadius = 8;
                Masking = true;

                InternalChildren = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both },
                    plusText = new OsuSpriteText
                    {
                        Text = "+",
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = OsuFont.GetFont(weight: FontWeight.Bold, size: 28),
                    },
                };
            }

            [BackgroundDependencyLoader]
            private void load(OverlayColourProvider colours)
            {
                this.colours = colours;
                background.Colour = colours.Light3;
                plusText.Colour = colours.Content2;
            }

            protected override bool OnClick(ClickEvent e)
            {
                Action?.Invoke();
                return true;
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.Colour = colours.Light2;
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e) => background.Colour = colours.Light3;

            public Action Action { get; set; } = null!;
        }

        private partial class ProfileCard : CompositeDrawable
        {
            private readonly LocalProfile profile;
            private readonly Bindable<string> activeProfile;
            private readonly LocalUserManager localUserManager;
            private readonly OverlayColourProvider colours;
            private readonly Action<string> requestDelete;

            private readonly Box background;
            private readonly OsuSpriteText nameText;

            public ProfileCard(LocalProfile profile, Bindable<string> activeProfile, LocalUserManager localUserManager, OverlayColourProvider colours, Action<string> requestDelete)
            {
                this.profile = profile;
                this.activeProfile = activeProfile;
                this.localUserManager = localUserManager;
                this.colours = colours;
                this.requestDelete = requestDelete;

                Width = 140;
                Height = 60;
                CornerRadius = 8;
                Masking = true;

                InternalChildren = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both },
                    nameText = new OsuSpriteText
                    {
                        Padding = new MarginPadding(10),
                        RelativeSizeAxes = Axes.X,
                        Font = OsuFont.GetFont(weight: FontWeight.Bold, size: 16),
                        Text = profile.Name,
                    },
                };

                activeProfile.BindValueChanged(_ => updateActive());
                updateActive();
            }

            private void updateActive()
            {
                bool isActive = activeProfile.Value == profile.Name;
                background.Colour = isActive ? colours.Colour1 : colours.Light3;
                nameText.Colour = isActive ? colours.Content1 : colours.Content2;
            }

            protected override bool OnHover(HoverEvent e)
            {
                background.Colour = colours.Light2;
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e) => updateActive();

            protected override bool OnClick(ClickEvent e)
            {
                localUserManager.SetActiveProfile(profile.Name);
                return true;
            }
        }
    }
}
