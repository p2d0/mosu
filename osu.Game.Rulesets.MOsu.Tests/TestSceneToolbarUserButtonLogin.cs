// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Platform;
using osu.Framework.Graphics.Containers;
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

            AddStep("SETUP", () =>
            {
                Ruleset.Value = ruleset.RulesetInfo;

                profileOverlay = new LocalUserProfileOverlay();
                Dependencies.CacheAs<LocalUserProfileOverlay>(profileOverlay);

                // Add LocalUserManager to hierarchy so Schedule() processes
                Add(new Container { localUserManager });

                toolbarButton = new ToolbarLocalUserButton();
                Add(toolbarButton);
            });
        }

        [Test]
        public void TestToolbarShowsPPAfterLogin()
        {
            // Simulate login via API.Login() which triggers EnsureDefaultProfile via BindValueChanged
            AddStep("simulate login", () =>
            {
                ((DummyAPIAccess)API).SkipSecondFactor();
                API.Login(MAIN_USER, "password");
            });

            // Create profile for logged-in user and set as active
            AddStep("set up user profile", () =>
            {
                localUserManager.AddProfile(MAIN_USER);
                localUserManager.SetActiveProfile(MAIN_USER);
            });

            // Load stats from scores and await completion
            AddStep("load statistics from scores", () =>
            {
                localUserManager.RefreshStatisticsAsync(ruleset.RulesetInfo);
            });

            // Wait for stats to be cached (async load)
            AddUntilStep("wait for stats to load", () =>
            {
                var stats = localUserManager.GetStatisticsFor(ruleset.RulesetInfo);
                return stats != null && stats.PP > 0;
            });

            // Wait for toolbar to update
            AddUntilStep("wait for toolbar update", () => toolbarButton.ppText.Text != "- pp");
            AddAssert("toolbar shows PP", () => { var t = toolbarButton.ppText.Text.ToString(); return t.EndsWith(" pp") && t != "- pp"; });
            AddAssert("toolbar shows correct username", () => toolbarButton.usernameText.Text == MAIN_USER);
        }
    }
}
