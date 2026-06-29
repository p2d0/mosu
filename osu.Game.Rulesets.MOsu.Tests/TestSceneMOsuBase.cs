using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Platform;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Rulesets.MOsu.Database;
using osu.Game.Rulesets.MOsu.UI.LocalUser;
using osu.Game.Rulesets.Osu;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.MOsu.Tests
{
    public abstract partial class TestSceneMOsuBase : OsuTestScene
    {
        protected DummyAPIAccess dummyAPI => (DummyAPIAccess)API;

        protected OsuRuleset ruleset = null!;
        protected LocalUserManager localUserManager = null!;
        protected MOsuRealmAccess mosuRealm = null!;

        [Resolved]
        private GameHost gameHost { get; set; } = null!;

        protected override bool UseFreshStoragePerRun => true;

        protected void CaptureScreenshot(string testName)
        {
            AddStep("screenshot", () =>
            {
                var fixtureName = GetType().Name.Replace("TestScene", "");
                ScreenshotHelper.Capture(gameHost, $"{fixtureName}_{testName}");
            });
            AddWaitStep("wait for screenshot", 1);
        }

        [BackgroundDependencyLoader]
        private void load(IAPIProvider api)
        {
            ruleset = new OsuRuleset();
            Dependencies.Cache(Realm);
            mosuRealm = new MOsuRealmAccess(LocalStorage);
            Dependencies.Cache(mosuRealm);
            Dependencies.Cache(localUserManager = new LocalUserManager(ruleset, Realm, mosuRealm, api));

            Realm.Write(r =>
            {
                if (r.Find<RulesetInfo>(ruleset.RulesetInfo.ShortName) == null)
                    r.Add(new RulesetInfo { OnlineID = 0, ShortName = ruleset.RulesetInfo.ShortName });
            });
        }
    }
}
