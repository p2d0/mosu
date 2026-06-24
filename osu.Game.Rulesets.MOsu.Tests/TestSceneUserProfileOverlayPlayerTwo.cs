using NUnit.Framework;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.MOsu.Tests
{
    public partial class TestSceneUserProfileOverlayPlayerTwo : TestSceneUserProfileOverlayBase
    {
        [Test]
        public void TestPlayerTwoProfile()
        {
            AddStep("show user", () => profile.ShowUser(new APIUser { Id = 2, Username = "PlayerTwo" }, ruleset.RulesetInfo));
            AddWaitStep("wait for content", 3);
        }
    }
}
