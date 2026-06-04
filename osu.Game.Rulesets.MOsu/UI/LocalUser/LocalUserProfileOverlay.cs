using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Input.Events;
using osu.Framework.Logging;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Cursor;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Overlays;
using osu.Game.Overlays.Profile;
using osu.Game.Overlays.Profile.Sections;
using osu.Game.Rulesets;
using osu.Game.Rulesets.MOsu.UI.LocalUser.Sections;
using osu.Game.Users;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.MOsu.UI.LocalUser
{
    [Cached]
    public partial class LocalUserProfileOverlay : FullscreenOverlay<LocalProfileHeader>
    {
        // Define these at class level so we can use them in the constructor AND async methods
        private readonly ProfileSectionsContainer sectionsContainer;
        private readonly ProfileSectionTabControl tabs;
        private readonly LoadingLayer loadingLayer;

        private ProfileSection? lastSection;
        private GetUserRequest? userReq;

        [Resolved]
        private LocalUserManager localUserManager { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        public LocalUserProfileOverlay()
            : base(OverlayColourScheme.Pink)
        {
            // 1. CONSTRUCT THE LAYOUT IMMEDIATELY
            // We do not wait for data to build the UI structure.

            // Create the container that holds sections
            sectionsContainer = new ProfileSectionsContainer
            {
                RelativeSizeAxes = Axes.Both,
                ExpandableHeader = Header, // Link the header immediately
                FixedHeader = tabs = new ProfileSectionTabControl
                {
                    RelativeSizeAxes = Axes.X,
                    Anchor = Anchor.TopCentre,
                    Origin = Anchor.TopCentre,
                },
                HeaderBackground = new Box
                {
                    Colour = ColourProvider.Background5,
                    RelativeSizeAxes = Axes.Both
                },
            };

            // 2. Add to base.Content (Visual Hierarchy)
            base.Content.AddRange(new Drawable[]
            {
                // The main scrolling/section content
                new OsuContextMenuContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = sectionsContainer
                },
                // The loading layer on top
                new PopoverContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = loadingLayer = new LoadingLayer(true)
                }
            });

            // Hook up tab switching logic immediately
            sectionsContainer.SelectedSection.ValueChanged += section =>
            {
                if (lastSection != section.NewValue)
                {
                    lastSection = section.NewValue;
                    tabs.Current.Value = lastSection!;
                }
            };

            tabs.Current.ValueChanged += section =>
            {
                if (lastSection == null)
                {
                    lastSection = sectionsContainer.Children.FirstOrDefault();
                    if (lastSection != null)
                        tabs.Current.Value = lastSection;
                    return;
                }

                if (lastSection != section.NewValue)
                {
                    lastSection = section.NewValue;
                    sectionsContainer.ScrollTo(lastSection);
                }
            };
        }

        // Keep the overlay active while loading
        public override bool IsPresent => base.IsPresent || Scheduler.HasPendingTasks;

        [BackgroundDependencyLoader]
        private void load()
        {
            localUserManager.ProfileChanged += _ =>
            {
                if (IsPresent)
                    Schedule(() => fetchAndSetContentForLocalUser(new osu.Game.Online.API.Requests.Responses.APIUser { Username = localUserManager.ActiveProfile.Value }, null));
            };

            // Also refresh when stats are updated (e.g., after gameplay)
            localUserManager.StatisticsUpdated += _ =>
            {
                if (IsPresent)
                    Schedule(() => fetchAndSetContentForLocalUser(new osu.Game.Online.API.Requests.Responses.APIUser { Username = localUserManager.ActiveProfile.Value }, null));
            };
        }

        protected override LocalProfileHeader CreateHeader() => new LocalProfileHeader();
        protected override Color4 BackgroundColour => ColourProvider.Background5;

        public void ShowUser(IUser userToShow, IRulesetInfo? userRuleset = null)
        {
            Show();

            // Schedule the data fetch
            Schedule(() => fetchAndSetContentForLocalUser(userToShow, userRuleset));
        }

        public void ToggleVisibilityUser(IUser userToShow, IRulesetInfo? userRuleset = null)
        {
            ToggleVisibility();

            // Schedule the data fetch
            Schedule(() => fetchAndSetContentForLocalUser(userToShow, userRuleset));
        }

        public async void fetchAndSetContentForLocalUser(IUser userToShow, IRulesetInfo? userRuleset)
        {
            userReq?.Cancel();
            loadingLayer.Show();

            try
            {
                // Clear previous sections but KEEP the container structure
                sectionsContainer.Clear();
                tabs.Clear();
                lastSection = null;

                var actualRuleset = rulesets.GetRuleset(userRuleset?.ShortName ?? @"mosususu").AsNonNull();

                // Async Data Fetch
                var userWithStats = await localUserManager.GetLocalUserWithStatisticsAsync(actualRuleset).ConfigureAwait(false);
                var userProfileData = new UserProfileData(userWithStats, actualRuleset);

                // Create Sections
                var localRanks = new LocalRanksSection();
                var newSections = new ProfileSection[] { localRanks };

                // Update UI on Main Thread
                Schedule(() =>
                {
                    Header.User.Value = userProfileData;

                    foreach (var sec in newSections)
                    {
                        sec.User.Value = userProfileData;
                        sectionsContainer.Add(sec);
                        tabs.AddItem(sec);
                    }

                    loadingLayer.Hide();
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to load local user data");
                Schedule(loadingLayer.Hide);
            }
        }

        // --- Internal Classes (Same as before) ---

        private partial class ProfileSectionTabControl : OsuTabControl<ProfileSection>
        {
            public ProfileSectionTabControl()
            {
                Height = 40;
                Padding = new MarginPadding { Horizontal = HORIZONTAL_PADDING };
                TabContainer.Spacing = new Vector2(20);
            }
            protected override TabItem<ProfileSection> CreateTabItem(ProfileSection value) => new ProfileSectionTabItem(value);
            protected override bool OnClick(ClickEvent e) => true;
            protected override bool OnHover(HoverEvent e) => true;

            private partial class ProfileSectionTabItem : TabItem<ProfileSection>
            {
                private OsuSpriteText text = null!;
                [Resolved] private OverlayColourProvider colourProvider { get; set; } = null!;
                public ProfileSectionTabItem(ProfileSection value) : base(value) { }
                [BackgroundDependencyLoader]
                private void load()
                {
                    AutoSizeAxes = Axes.Both;
                    Anchor = Anchor.CentreLeft;
                    Origin = Anchor.CentreLeft;
                    InternalChild = text = new OsuSpriteText { Text = Value.Title };
                    updateState();
                }
                protected override void OnActivated() => updateState();
                protected override void OnDeactivated() => updateState();
                protected override bool OnHover(HoverEvent e) { updateState(); return true; }
                protected override void OnHoverLost(HoverLostEvent e) => updateState();
                private void updateState()
                {
                    text.Font = OsuFont.Default.With(size: 14, weight: Active.Value ? FontWeight.SemiBold : FontWeight.Regular);
                    Colour4 textColour = IsHovered ? colourProvider.Light1 : (Active.Value ? colourProvider.Content1 : colourProvider.Light2);
                    text.FadeColour(textColour, 300, Easing.OutQuint);
                }
            }
        }

        private partial class ProfileSectionsContainer : SectionsContainer<ProfileSection>
        {
            private OverlayScrollContainer scroll = null!;
            public ProfileSectionsContainer() { RelativeSizeAxes = Axes.Both; }
            protected override UserTrackingScrollContainer CreateScrollContainer() => scroll = new OverlayScrollContainer();
            protected override FlowContainer<ProfileSection> CreateScrollContentContainer() => new ReverseChildIDFillFlowContainer<ProfileSection>
            {
                Direction = FillDirection.Vertical,
                AutoSizeAxes = Axes.Y,
                RelativeSizeAxes = Axes.X,
                Spacing = new Vector2(0, 10),
                Padding = new MarginPadding { Horizontal = 10 },
                Margin = new MarginPadding { Bottom = 10 },
            };
            protected override void LoadComplete()
            {
                base.LoadComplete();
                AddInternal(scroll.Button.CreateProxy());
            }
        }
        public bool IsDisposedPublic => IsDisposed;
    }
}
