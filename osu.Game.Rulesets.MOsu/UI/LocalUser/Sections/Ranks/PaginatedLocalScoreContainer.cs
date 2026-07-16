using osu.Framework.Graphics.Containers;
using osu.Game.Online.API.Requests;
using System;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Online.API.Requests.Responses;
using System.Collections.Generic;
using osu.Game.Online.API;
using osu.Framework.Allocation;
using osu.Framework.Localisation;
using APIUser = osu.Game.Online.API.Requests.Responses.APIUser;
using osu.Game.Scoring;
using System.Threading.Tasks;
using osu.Framework.Logging;
using System.Linq;
using osu.Game.Overlays.Profile;

namespace osu.Game.Rulesets.MOsu.UI.LocalUser.Sections.Ranks
{
    public partial class PaginatedLocalScoreContainer : PaginatedLocalProfileSubsection<ScoreInfo>
    {
        private readonly ScoreType type;

        [Resolved]
        private LocalUserManager LocalUserManager { get; set; } = null!;

        public PaginatedLocalScoreContainer(ScoreType type, Bindable<UserProfileData?> user, LocalisableString headerText)
            : base(user, headerText)
        {
            this.type = type;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            ItemsContainer.Direction = FillDirection.Vertical;
        }

        protected override int GetCount(APIUser user)
        {
            // Use actual score count from LocalUserManager, not APIUser.ScoresBestCount (which is 0 for local users)
            if (User.Value is var ud && ud != null)
            {
                var username = user.Username;
                switch (type)
                {
                    case ScoreType.Recent:
                        return LocalUserManager.GetRecentScores(username, ud.Ruleset).Count; case ScoreType.Best:
                    default:
                        return LocalUserManager.GetBestScores(username, ud.Ruleset).Count;
                }
            }
            return 0;
        }

        protected override void OnItemsReceived(List<ScoreInfo> items)
        {
            if (CurrentPage == null || CurrentPage?.Offset == 0)
                drawableItemIndex = 0;

            base.OnItemsReceived(items);
        }

        protected override async Task<List<ScoreInfo>> CreateTask(UserProfileData? user) {
            if (user == null) return new List<ScoreInfo>();
            Logger.Log($"Loading local scores for {user.User.Id} {user.User.Username}, ruleset: {user.Ruleset.ShortName}", level: LogLevel.Debug);
            return await getScores(user).ConfigureAwait(false);
        }
        private Task<List<ScoreInfo>> getScores(UserProfileData user){
            return Task.Run(() => {
                switch (type) {
                    case ScoreType.Recent:
                        return LocalUserManager.GetRecentScores(user.User.Username, user.Ruleset);
                    case ScoreType.Best:
                    default:
                        return LocalUserManager.GetBestScores(user.User.Username,user.Ruleset);

                }});
                }

        private int drawableItemIndex;

        protected override Drawable CreateDrawableItem(ScoreInfo model)
        {
            var ruleset = new OsuRuleset();
            switch (type)
            {
                default:
                    return new DrawableProfileLocalScore(model, ruleset);

                case ScoreType.Best:
                    return new DrawableProfileLocalWeightedScore(model, Math.Pow(0.95, drawableItemIndex++), ruleset);
            }
        }
    }
}
