// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Overlays.Toolbar;
using osu.Game.Rulesets.MOsu.Database;
using osu.Game.Rulesets.MOsu.Models;
using osu.Game.Rulesets.MOsu.UI.LocalUser;
using osu.Game.Rulesets.MOsu.UI.Toolbar;
using osu.Game.Scoring;
using osu.Game.Users;
using osu.Game.Tests.Visual;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Models;

namespace osu.Game.Rulesets.MOsu.Tests
{
    [TestFixture]
    public partial class TestSceneToolbarUserButtonLogin : OsuTestScene
    {
        protected override bool UseFreshStoragePerRun => true;

        [Resolved]
        private GameHost gameHost { get; set; } = null!;

        [TearDown]
        public void TearDownScreenshot() => ScreenshotHelper.Capture(gameHost);

        private LocalUserManager localUserManager = null!;
        private OsuRuleset ruleset = null!;
        private ToolbarLocalUserButton toolbarButton = null!;
        private LocalUserProfileOverlay profileOverlay = null!;
        private RealmAccess realm = null!;
        private MOsuRealmAccess mosuRealm = null!;

        private const string MAIN_USER = "TestUser";

        protected override Ruleset CreateRuleset() => new OsuRuleset();

        [BackgroundDependencyLoader]
        private void load(RealmAccess r, IAPIProvider api)
        {
            realm = r;
            ruleset = new OsuRuleset();
            mosuRealm = new MOsuRealmAccess(LocalStorage);
            Dependencies.Cache(mosuRealm);
            Dependencies.Cache(localUserManager = new LocalUserManager(ruleset, r, mosuRealm, api));
        }

        [SetUpSteps]
        public void SetUp()
        {
            AddStep("set up dummy API user", () =>
            {
                ((DummyAPIAccess)API).LocalUser.Value = new APIUser
                {
                    Username = MAIN_USER,
                    Id = 1,
                    CountryCode = CountryCode.US,
                };
            });

            AddStep("SETUP", () =>
            {
                localUserManager.EnsureDefaultProfile();
                localUserManager.SetActiveProfile(MAIN_USER);
                Ruleset.Value = ruleset.RulesetInfo;

                profileOverlay = new LocalUserProfileOverlay();
                Dependencies.CacheAs<LocalUserProfileOverlay>(profileOverlay);

                toolbarButton = new ToolbarLocalUserButton();
                Add(toolbarButton);
            });
        }

        [Test]
        public void TestToolbarShowsPPAfterLogin()
        {
            // Seed scores for the user
            AddStep("seed scores", () =>
            {
                realm.Write(r =>
                {
                    r.RemoveAll<ScoreInfo>();
                    r.RemoveAll<BeatmapInfo>();

                    var rs = r.Find<RulesetInfo>(ruleset.RulesetInfo.ShortName) ?? ruleset.RulesetInfo;
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

            // Simulate stats being loaded (as InitialiseStatisticsAsync would do)
            AddStep("load stats into cache", () =>
            {
                localUserManager.UpdateStatistics(new UserStatistics { PP = 150m, TotalScore = 1000000, PlayCount = 1 }, ruleset.RulesetInfo);
            });

            AddAssert("toolbar shows correct PP", () => toolbarButton.ppText.Text == "150 pp");
            AddAssert("toolbar shows correct username", () => toolbarButton.usernameText.Text == MAIN_USER);
        }
    }
}
