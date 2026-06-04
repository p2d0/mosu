using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Graphics.UserInterface;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Rulesets.MOsu.Models;
using osu.Game.Users;
using osuTK;

namespace osu.Game.Rulesets.MOsu.UI.LocalUser
{
    public partial class ProfileCardRow : CompositeDrawable
    {
        private readonly Bindable<string> activeProfile;
        private readonly LocalUserManager localUserManager;

        private FillFlowContainer cardsContainer = null!;

        public Action<string> RequestAddProfile { get; set; } = null!;
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
                Spacing = new Vector2(8),
                Padding = new MarginPadding { Top = 8, Bottom = 8 },
            };
        }

        [BackgroundDependencyLoader]
        private void load(OverlayColourProvider colours)
        {
            foreach (var profile in localUserManager.GetProfiles())
                addCard(profile, colours);

            addButton = new AddProfileButton
            {
                Action = showAddPopover,
            };
            cardsContainer.Add(addButton);
        }

        private void addCard(LocalProfile profile, OverlayColourProvider colours)
        {
            var card = new ProfilePill(profile, activeProfile, localUserManager, colours, RequestDeleteProfile);
            cardsContainer.Insert(cardsContainer.Count - 1, card);
        }

        public void Refresh()
        {
            cardsContainer.Clear();
            var colours = new OverlayColourProvider(OverlayColourScheme.Pink);
            foreach (var profile in localUserManager.GetProfiles())
                addCard(profile, colours);
            addButton = new AddProfileButton
            {
                Action = showAddPopover,
            };
            cardsContainer.Add(addButton);
        }

        private AddProfileButton addButton = null!;

        private void showAddPopover()
        {
            addButton.CurrentPopover = new AddProfilePopover
            {
                OnAdd = name =>
                {
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        localUserManager.AddProfile(name.Trim());
                        Refresh();
                    }
                },
            };
            addButton.ShowPopover();
        }

        // + button
        private partial class AddProfileButton : CompositeDrawable, IHasPopover
        {
            private readonly Box background;
            private readonly OsuSpriteText plusText;
            private OverlayColourProvider colours = null!;

            public Popover CurrentPopover { get; set; } = null!;

            public AddProfileButton()
            {
                Height = 32;
                Width = 32;
                CornerRadius = 16;
                Masking = true;

                InternalChildren = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both },
                    plusText = new OsuSpriteText
                    {
                        Text = "+",
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Font = OsuFont.GetFont(weight: FontWeight.Bold, size: 18),
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

            public Popover GetPopover() => CurrentPopover;
        }

        // Profile pill: name + PP
        private partial class ProfilePill : CompositeDrawable
        {
            private readonly string profileName;
            private readonly Bindable<string> activeProfile;
            private readonly LocalUserManager localUserManager;
            private readonly OverlayColourProvider colours;
            private readonly Action<string> requestDelete;

            private readonly Box background;
            private readonly OsuSpriteText nameText;
            private readonly OsuSpriteText ppText;

            public ProfilePill(LocalProfile profile, Bindable<string> activeProfile, LocalUserManager localUserManager, OverlayColourProvider colours, Action<string> requestDelete)
            {
                profileName = profile.Name;
                this.activeProfile = activeProfile;
                this.localUserManager = localUserManager;
                this.colours = colours;
                this.requestDelete = requestDelete;

                Height = 32;
                CornerRadius = 16;
                Masking = true;
                AutoSizeAxes = Axes.X;

                InternalChildren = new Drawable[]
                {
                    background = new Box { RelativeSizeAxes = Axes.Both },
                    new FillFlowContainer
                    {
                        AutoSizeAxes = Axes.Both,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(6),
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Padding = new MarginPadding { Horizontal = 16 },
                        Children = new Drawable[]
                        {
                            nameText = new OsuSpriteText
                            {
                                Font = OsuFont.GetFont(weight: FontWeight.Bold, size: 13),
                            },
                            ppText = new OsuSpriteText
                            {
                                Font = OsuFont.GetFont(weight: FontWeight.SemiBold, size: 11),
                            },
                        }
                    },
                };

                nameText.Text = profileName;
                updatePP();

                activeProfile.BindValueChanged(_ => updateActive());
                localUserManager.ProfileChanged += _ => updateActive();
                localUserManager.StatisticsUpdated += _ => updatePP();
                updateActive();
            }

            private void updateActive()
            {
                bool isActive = activeProfile.Value == profileName;
                background.Colour = isActive ? colours.Colour1 : colours.Light3;
                nameText.Colour = isActive ? colours.Content1 : colours.Content2;
            }

            private void updatePP()
            {
                var stats = localUserManager.GetStatisticsForProfile(profileName, localUserManager.RulesetInfo);
                ppText.Text = stats != null ? $"\u00B7 {stats.PP:N0}pp" : "";
            }

            protected override bool OnHover(HoverEvent e)
            {
                if (activeProfile.Value != profileName)
                    background.Colour = colours.Light2;
                return base.OnHover(e);
            }

            protected override void OnHoverLost(HoverLostEvent e) => updateActive();

            protected override bool OnClick(ClickEvent e)
            {
                localUserManager.SetActiveProfile(profileName);
                return true;
            }

            protected override bool OnDoubleClick(DoubleClickEvent e)
            {
                if (localUserManager.GetProfiles().Count > 1)
                    requestDelete?.Invoke(profileName);
                return true;
            }
        }

        // Popover for adding a profile
        public partial class AddProfilePopover : OsuPopover
        {
            public Action<string> OnAdd { get; set; } = null!;

            private FocusedTextBox textBox;

            public AddProfilePopover()
            {
                AutoSizeAxes = Axes.Both;
                Origin = Anchor.TopCentre;

                Child = new FillFlowContainer
                {
                    Direction = FillDirection.Vertical,
                    AutoSizeAxes = Axes.Both,
                    Spacing = new Vector2(10),
                    Padding = new MarginPadding(12),
                    Children = new Drawable[]
                    {
                        new OsuSpriteText
                        {
                            Text = "New profile",
                            Font = OsuFont.GetFont(weight: FontWeight.Bold, size: 14),
                        },
                        textBox = new FocusedTextBox
                        {
                            Width = 220,
                            PlaceholderText = "Profile name",
                            SelectAllOnFocus = true,
                        },
                    }
                };

                textBox.OnCommit += (_, _) => confirm();
            }

            protected override void PopIn()
            {
                base.PopIn();
                textBox.TakeFocus();
            }

            private void confirm()
            {
                var name = textBox.Text.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    OnAdd?.Invoke(name);
                this.HidePopover();
            }
        }
    }
}
