// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Overlays.Toolbar;
using osu.Game.Rulesets.MOsu.Database;
using osu.Game.Rulesets.MOsu.UI.LocalUser;
using osu.Game.Rulesets.MOsu.UI.Toolbar;
using osu.Game.Users;
using osu.Game.Tests.Visual;
using osu.Game.Online.API.Requests.Responses;

namespace osu.Game.Rulesets.MOsu.Tests
{
    [TestFixture]
    public partial class TestSceneToolbarUserButton : OsuTestScene
    {
        [Resolved]
        private GameHost gameHost { get; set; } = null!;

        [TearDown]
        public void TearDownScreenshot() => ScreenshotHelper.Capture(gameHost);
        private LocalUserManager localUserManager = null!;
        private OsuRuleset ruleset = null!;
        private ToolbarLocalUserButton toolbarButton = null!;
        private LocalUserProfileOverlay profileOverlay = null!;

        private const string MAIN_USER = "Somebody";

        protected override Ruleset CreateRuleset() => new OsuRuleset();

        [BackgroundDependencyLoader]
        private void load(RealmAccess realm, IAPIProvider api)
        {
            ruleset = new OsuRuleset();
            var mosuRealm = new MOsuRealmAccess(LocalStorage);
            Dependencies.Cache(mosuRealm);
            Dependencies.Cache(localUserManager = new LocalUserManager(ruleset, realm, mosuRealm, api));
        }

        [SetUpSteps]
        public void SetUp()
        {
            AddStep("SETUP", () =>
            {
                localUserManager.EnsureDefaultProfile();
                localUserManager.AddProfile("Alt");
                localUserManager.SetActiveProfile(MAIN_USER);

                Ruleset.Value = ruleset.RulesetInfo;

                profileOverlay = new LocalUserProfileOverlay();
                Dependencies.CacheAs<LocalUserProfileOverlay>(profileOverlay);

                toolbarButton = new ToolbarLocalUserButton();
                Add(toolbarButton);
            });
        }

        [Test]
        public void TestToolbarUpdatesOnProfileSwitch()
        {
            AddAssert("toolbar initial username", () => toolbarButton.usernameText.Text.ToString() == MAIN_USER);

            AddStep("set main profile stats", () =>
            {
                localUserManager.SetActiveProfile(MAIN_USER);
                localUserManager.UpdateStatistics(new UserStatistics { PP = 100m }, ruleset.RulesetInfo);
            });

            AddAssert("main has 100 pp", () => localUserManager.GetStatisticsFor(ruleset.RulesetInfo)?.PP == 100m);

            AddStep("set alt profile stats", () =>
            {
                localUserManager.SetActiveProfile("Alt");
                localUserManager.UpdateStatistics(new UserStatistics { PP = 999m }, ruleset.RulesetInfo);
            });

            AddAssert("alt has 999 pp", () => localUserManager.GetStatisticsFor(ruleset.RulesetInfo)?.PP == 999m);

            AddStep("switch to main", () => localUserManager.SetActiveProfile(MAIN_USER));
            AddAssert("toolbar shows main username", () => toolbarButton.usernameText.Text.ToString() == MAIN_USER);
            AddAssert("toolbar shows main pp", () => toolbarButton.ppText.Text.ToString().Contains("100"));

            AddStep("switch to alt", () => localUserManager.SetActiveProfile("Alt"));
            AddAssert("toolbar shows alt username", () => toolbarButton.usernameText.Text.ToString() == "Alt");
            AddAssert("toolbar shows alt pp", () => toolbarButton.ppText.Text.ToString().Contains("999"));

            AddStep("switch back to main", () => localUserManager.SetActiveProfile(MAIN_USER));
            AddAssert("toolbar shows main username again", () => toolbarButton.usernameText.Text.ToString() == MAIN_USER);
            AddAssert("toolbar shows main pp again", () => toolbarButton.ppText.Text.ToString().Contains("100"));
        }

    }
}
