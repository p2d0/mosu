using System.Diagnostics;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Localisation;
using osu.Game.Graphics;
using osu.Game.Overlays;
using osu.Game.Overlays.Profile;
using osu.Game.Overlays.Profile.Header;
using osu.Game.Overlays.Profile.Header.Components;
using osu.Game.Resources.Localisation.Web;
using osu.Game.Rulesets.MOsu.UI.LocalUser.Header;

namespace osu.Game.Rulesets.MOsu.UI.LocalUser
{
    public partial class LocalProfileHeader : TabControlOverlayHeader<LocalisableString>
    {
        public Bindable<UserProfileData?> User = new Bindable<UserProfileData?>();

        private CentreHeaderContainer centreHeaderContainer;
        private DetailHeaderContainer detailHeaderContainer;
        private LocalTopHeaderContainer localTopHeader = null!;
        private FillFlowContainer contentContainer = null!;

        [Resolved]
        private LocalUserManager localUserManager { get; set; } = null!;

        public LocalProfileHeader()
        {
            ContentSidePadding = WaveOverlayContainer.HORIZONTAL_PADDING;

            TabControl.AddItem(LayoutStrings.HeaderUsersShow);

            // todo: pending implementation.
            // TabControl.AddItem(LayoutStrings.HeaderUsersModding);

            // Haphazardly guaranteed by OverlayHeader constructor (see CreateBackground / CreateContent).
            Debug.Assert(centreHeaderContainer != null);
            Debug.Assert(detailHeaderContainer != null);
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            localTopHeader.ProfileCardRow.RequestAddProfile = name =>
            {
                if (!string.IsNullOrWhiteSpace(name))
                    localUserManager.AddProfile(name);
            };
            localTopHeader.ProfileCardRow.RequestDeleteProfile = name => localUserManager.RemoveProfile(name);
            localUserManager.ProfileChanged += _ => Schedule(() => localTopHeader.ProfileCardRow.Refresh());
            localUserManager.ProfilesChanged += () => Schedule(() => localTopHeader.ProfileCardRow.Refresh());
        }

        protected override Drawable CreateBackground() => Empty();

        protected override Drawable CreateContent() => contentContainer = new FillFlowContainer
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Children = new Drawable[]
            {
                localTopHeader = new LocalTopHeaderContainer
                {
                    RelativeSizeAxes = Axes.X,
                    User = { BindTarget = User },
                },
                new BannerHeaderContainer
                {
                    User = { BindTarget = User },
                },
                new BadgeHeaderContainer
                {
                    RelativeSizeAxes = Axes.X,
                    User = { BindTarget = User },
                },
                detailHeaderContainer = new LocalDetailHeaderContainer
                {
                    RelativeSizeAxes = Axes.X,
                    User = { BindTarget = User },
                },
                centreHeaderContainer = new CentreHeaderContainer
                {
                    RelativeSizeAxes = Axes.X,
                    User = { BindTarget = User },
                },
            }
        };

        protected override OverlayTitle CreateTitle() => new ProfileHeaderTitle();

        // protected override Drawable CreateTabControlContent() => new ProfileRulesetSelector
        // {
        //     User = { BindTarget = User }
        // };

        private partial class ProfileHeaderTitle : OverlayTitle
        {
            public ProfileHeaderTitle()
            {
                Title = PageTitleStrings.MainUsersControllerDefault;
                Icon = OsuIcon.Player;
            }
        }
    }
}
