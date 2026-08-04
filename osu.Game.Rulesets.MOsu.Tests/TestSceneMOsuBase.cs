using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Platform;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Rulesets.MOsu.Configuration;
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
        protected MOsuRulesetConfigManager config = null!;

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

            // MOsuRulesetConfigManager must be constructed on the update thread (it loads from the realm in its ctor).
            // Under dotnet test the game host already caches one, so reuse it when present.
            Scheduler.Add(() =>
            {
                config = (MOsuRulesetConfigManager?)Dependencies.Get(typeof(MOsuRulesetConfigManager))
                      ?? new MOsuRulesetConfigManager(new SettingsStore(Realm), ruleset.RulesetInfo);
                Dependencies.Cache(localUserManager = new LocalUserManager(ruleset, Realm, config, api));
            });

            Realm.Write(r =>
            {
                if (r.Find<RulesetInfo>(ruleset.RulesetInfo.ShortName) == null)
                    r.Add(new RulesetInfo { OnlineID = 0, ShortName = ruleset.RulesetInfo.ShortName });
            });
        }
    }
}
