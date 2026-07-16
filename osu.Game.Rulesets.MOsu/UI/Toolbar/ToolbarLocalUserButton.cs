using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Platform;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Input.Events;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Online;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays.Toolbar;
using osu.Game.Rulesets;
using osu.Game.Rulesets.MOsu.Extensions;
using osu.Game.Rulesets.MOsu.UI.LocalUser;
using osu.Game.Users.Drawables;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.MOsu.UI.Toolbar
{
    public partial class ToolbarLocalUserButton : ToolbarOverlayToggleButton
    {
        // private UpdateableAvatar avatar;
        internal OsuSpriteText usernameText = null!;
        internal OsuSpriteText ppText = null!;

        [Resolved]
        private IBindable<RulesetInfo> ruleset { get; set; } = null!;

        [Resolved]
        private LocalUserManager statisticsProvider { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private GameHost host { get; set; } = null!;

        private LocalUserProfileOverlay? overlay;

        public ToolbarLocalUserButton()
        {
            ButtonContent.AutoSizeAxes = Axes.X;
            Alpha = 0;
        }

        [BackgroundDependencyLoader]
        private void load(OsuColour colours)
        {
            // 1. Define Layout using Flow (matches your example)
            Flow.AutoSizeAxes = Axes.X;
            Flow.Direction = FillDirection.Horizontal;
            Flow.Spacing = new Vector2(10);

            // Flow.Add(new UpdateableAvatar(isInteractive: false)
            // {
            //     Size = new Vector2(32),
            //     Anchor = Anchor.CentreLeft,
            //     Origin = Anchor.CentreLeft,
            //     Masking = true,
            //     CornerRadius = 4,
            // });

            Flow.Add(new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft,
                Direction = FillDirection.Vertical,
                Children = new Drawable[]
                {
                    usernameText = new OsuSpriteText
                    {
                        Font = OsuFont.GetFont(weight: FontWeight.Bold, size: 14),
                    },
                    ppText = new OsuSpriteText
                    {
                        Font = OsuFont.GetFont(weight: FontWeight.SemiBold, size: 12),
                        Colour = colours.BlueLighter,
                    }
                }
            });

            // 2. Bind Data
            var localUser = api.LocalUser.GetBoundCopy();
            localUser.BindValueChanged(u => updateDisplay(u.NewValue), true);

            // Also update when active profile changes
            statisticsProvider.ProfileChanged += _ => updateDisplay(api.LocalUser.Value!);

            ruleset.BindValueChanged(r =>
            {
                bool isMOsu = r.NewValue.ShortName == OsuRuleset.SHORT_NAME;
                this.FadeTo(isMOsu ? 1 : 0, 200);
                if (isMOsu) updatePP();
            }, true);

            if (statisticsProvider != null)
                statisticsProvider.StatisticsUpdated += onStatisticsUpdated;
        }

        // 3. Handle Click (Open LocalUserProfileOverlay)
        protected override bool OnClick(ClickEvent e)
        {
            overlay ??= host.Dependencies.Get<LocalUserProfileOverlay>();
            overlay?.ToggleVisibilityUser(api.LocalUser.Value, ruleset.Value);
            return true;
        }

        // 4. Update Logic
        private void onStatisticsUpdated(UserStatisticsUpdate update)
        {
            if (update.Ruleset.Equals(ruleset.Value))
                Schedule(updatePP);
        }

        private void updateDisplay(APIUser user)
        {
            usernameText.Text = statisticsProvider.ActiveProfile.Value ?? user.Username;
            updatePP();
        }

        private void updatePP()
        {
            // if (api.LocalUser.Value.Id <= 1) return;

            var stats = statisticsProvider?.GetStatisticsFor(ruleset.Value);
            ppText.Text = stats != null ? $"{stats.PP:N0} pp" : "- pp";
        }

        protected override void Dispose(bool isDisposing)
        {
            if (statisticsProvider != null)
                statisticsProvider.StatisticsUpdated -= onStatisticsUpdated;
            base.Dispose(isDisposing);
        }
    }
}
