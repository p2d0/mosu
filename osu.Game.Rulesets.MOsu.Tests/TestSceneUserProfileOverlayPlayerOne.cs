using NUnit.Framework;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.MOsu.Tests
{
    public partial class TestSceneUserProfileOverlayPlayerOne : TestSceneUserProfileOverlayBase
    {
        [Test]
        public void TestPlayerOneProfile()
        {
            AddStep("show user", () => profile.ShowUser(new APIUser { Id = 1, Username = "PlayerOne" }, ruleset.RulesetInfo));
            AddWaitStep("wait for content", 3);
        }
    }
}
