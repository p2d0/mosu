using NUnit.Framework;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.MOsu.Tests
{
    public partial class TestSceneUserProfileOverlayProfileSwitching : TestSceneUserProfileOverlayBase
    {
        [Test]
        public void TestProfileSwitching()
        {
            AddStep("show PlayerOne", () => profile.ShowUser(new APIUser { Id = 1, Username = "PlayerOne" }, ruleset.RulesetInfo));
            AddWaitStep("wait for content", 2);
            AddStep("switch to PlayerTwo", () => profile.ShowUser(new APIUser { Id = 2, Username = "PlayerTwo" }, ruleset.RulesetInfo));
            AddWaitStep("wait for switch", 2);
        }
    }
}
