using System;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Overlays.Toolbar;
using osu.Game.Rulesets.MOsu.UI.LocalUser;
using osu.Game.Rulesets.MOsu.UI.Toolbar;
using osu.Game.Models;
using osu.Game.Scoring;
using osu.Game.Users;

namespace osu.Game.Rulesets.MOsu.Tests
{
    [TestFixture]
    public partial class TestSceneToolbarUserButton : TestSceneMOsuBase
    {
        private ToolbarLocalUserButton toolbarButton = null!;
        private LocalUserProfileOverlay profileOverlay = null!;

        private const string MAIN_USER = "TestUser";

        protected override Ruleset CreateRuleset() => new OsuRuleset();

        [SetUpSteps]
        public void SetUp()
        {
            AddStep("set up", () =>
            {
                localUserManager.EnsureDefaultProfile();
                localUserManager.AddProfile("Alt");
                localUserManager.SetActiveProfile(MAIN_USER);

                Ruleset.Value = ruleset.RulesetInfo;

                profileOverlay = new LocalUserProfileOverlay();
                try { Dependencies.CacheAs<LocalUserProfileOverlay>(profileOverlay); } catch { }

                toolbarButton = new ToolbarLocalUserButton();
                Add(toolbarButton);
            });
        }

        [Test]
        public void TestToolbarUpdatesOnProfileSwitch()
        {
            AddStep("set main profile stats", () =>
            {
                localUserManager.SetActiveProfile(MAIN_USER);
                localUserManager.UpdateStatistics(new UserStatistics { PP = 100m }, ruleset.RulesetInfo);
            });

            AddStep("set alt profile stats", () =>
            {
                localUserManager.SetActiveProfile("Alt");
                localUserManager.UpdateStatistics(new UserStatistics { PP = 999m }, ruleset.RulesetInfo);
            });

            AddStep("switch to main", () => localUserManager.SetActiveProfile(MAIN_USER));
            AddStep("switch to alt", () => localUserManager.SetActiveProfile("Alt"));
            AddStep("switch back to main", () => localUserManager.SetActiveProfile(MAIN_USER));
            CaptureScreenshot("ToolbarUpdatesOnProfileSwitch");
        }

        [Test]
        public void TestToolbarShowsPPAfterLogin()
        {
            AddStep("seed scores", () =>
            {
                Realm.Write(r =>
                {
                    r.RemoveAll<ScoreInfo>();
                    r.RemoveAll<BeatmapInfo>();

                    var rs = r.Find<RulesetInfo>(ruleset.RulesetInfo.ShortName);
                    var beatmap = new BeatmapInfo
                    {
                        DifficultyName = "Test Difficulty",
                        Ruleset = r.Find<RulesetInfo>(rs.ShortName) ?? rs,
                        Difficulty = new BeatmapDifficulty { OverallDifficulty = 5, CircleSize = 4, ApproachRate = 5, DrainRate = 5 },
                        Metadata = new BeatmapMetadata { Title = "Test Beatmap", Artist = "Test Artist" },
                    };
                    var user = new RealmUser { Username = MAIN_USER, OnlineID = 1 };

                    r.Add(new ScoreInfo(beatmap, rs, user)
                    {
                        TotalScore = 1000000,
                        MaxCombo = 50,
                        Accuracy = 0.95,
                        PP = 150.5,
                        Rank = ScoreRank.S,
                        Date = DateTimeOffset.Now,
                        DeletePending = false,
                        Mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>(),
                    });
                });
            });

            AddStep("simulate login", () =>
            {
                dummyAPI.SkipSecondFactor();
                API.Login(MAIN_USER, "password");
            });

            AddStep("set up user profile", () =>
            {
                localUserManager.AddProfile(MAIN_USER);
                localUserManager.SetActiveProfile(MAIN_USER);
            });

            AddStep("load statistics from scores", () =>
            {
                localUserManager.RefreshStatisticsAsync(ruleset.RulesetInfo);
            });

            AddUntilStep("wait for stats to load", () =>
            {
                var stats = localUserManager.GetStatisticsFor(ruleset.RulesetInfo);
                return stats != null && stats.PP > 0;
            });

            AddUntilStep("wait for toolbar update", () => toolbarButton.ppText.Text != "- pp");
            AddAssert("toolbar shows PP", () => { var t = toolbarButton.ppText.Text.ToString(); return t.EndsWith(" pp") && t != "- pp"; });
            AddAssert("toolbar shows correct username", () => toolbarButton.usernameText.Text == MAIN_USER);
            CaptureScreenshot("ToolbarShowsPPAfterLogin");
        }
    }
}
