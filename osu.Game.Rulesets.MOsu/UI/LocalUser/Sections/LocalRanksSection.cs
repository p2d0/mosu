using osu.Game.Resources.Localisation.Web;
using osu.Framework.Localisation;
using osu.Game.Online.API.Requests;
using osu.Game.Overlays.Profile;
using osu.Game.Rulesets.MOsu.UI.LocalUser.Sections.Ranks;

namespace osu.Game.Rulesets.MOsu.UI.LocalUser.Sections
{
    public partial class LocalRanksSection : ProfileSection
    {
        public override LocalisableString Title => UsersStrings.ShowExtraTopRanksTitle;

        public override string Identifier => @"top_ranks";

        public LocalRanksSection()
        {
            Children = new[]
            {
                new PaginatedLocalScoreContainer(ScoreType.Recent, User, UsersStrings.ShowExtraRecentActivityTitle),
                new PaginatedLocalScoreContainer(ScoreType.Best, User, UsersStrings.ShowExtraTopRanksBestTitle),
            };
        }
    }
}
