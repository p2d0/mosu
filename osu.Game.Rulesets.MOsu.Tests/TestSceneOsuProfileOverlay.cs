using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Rulesets.MOsu.Database;
using osu.Game.Rulesets.MOsu.Models;
using osu.Game.Rulesets.MOsu.UI.LocalUser;
using osu.Game.Rulesets.MOsu.UI.LocalUser.Sections.Ranks;
using osu.Game.Rulesets.Osu;
using osu.Game.Scoring;
using osu.Game.Tests.Visual;
using osu.Game.Models;
using osu.Game.Users;

namespace osu.Game.Rulesets.MOsu.Tests
{
    [TestFixture]
    public partial class TestSceneUserProfileOverlay : OsuTestScene
    {
        private DummyAPIAccess dummyAPI => (DummyAPIAccess)API;

        private LocalUserProfileOverlay profile = null!;
        private OsuRuleset ruleset = null!;
        private LocalUserManager localUserManager = null!;
        private MOsuRealmAccess mosuRealm = null!;
        private RealmAccess realm = null!;

        [Resolved]
        private GameHost gameHost { get; set; } = null!;

        [TearDown]
        public void TearDownScreenshot() => ScreenshotHelper.Capture(gameHost);

        protected override bool UseFreshStoragePerRun => true;

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
                dummyAPI.LocalUser.Value = new APIUser
                {
                    Username = "TestUser",
                    Id = 1,
                    CountryCode = CountryCode.US,
                };
            });

            AddStep("create test profiles", () =>
            {
                mosuRealm.Write(r =>
                {
                    r.RemoveAll<LocalProfile>();
                    r.Add(new LocalProfile { Name = "PlayerOne", IsActive = true });
                    r.Add(new LocalProfile { Name = "PlayerTwo", IsActive = false });
                });
            });

            AddStep("seed scores for PlayerOne", () =>
            {
                realm.Write(r =>
                {
                    r.RemoveAll<ScoreInfo>();
                    r.RemoveAll<BeatmapInfo>();

                    var rs = r.Find<RulesetInfo>(ruleset.RulesetInfo.ShortName) ?? ruleset.RulesetInfo;

                    r.Add(createScore(r, rs, "PlayerOne", "Difficulty 1", 150.5, ScoreRank.S, DateTimeOffset.Now.AddDays(-1)));
                    r.Add(createScore(r, rs, "PlayerOne", "Difficulty 2", 120.3, ScoreRank.A, DateTimeOffset.Now.AddDays(-2)));
                    r.Add(createScore(r, rs, "PlayerOne", "Difficulty 3", 95.0, ScoreRank.A, DateTimeOffset.Now));
                });
            });

            AddStep("seed scores for PlayerTwo", () =>
            {
                realm.Write(r =>
                {
                    var rs = r.Find<RulesetInfo>(ruleset.RulesetInfo.ShortName) ?? ruleset.RulesetInfo;

                    r.Add(createScore(r, rs, "PlayerTwo", "PlayerTwo Map 1", 200.0, ScoreRank.X, DateTimeOffset.Now.AddDays(-3)));
                    r.Add(createScore(r, rs, "PlayerTwo", "PlayerTwo Map 2", 80.2, ScoreRank.B, DateTimeOffset.Now));
                });
            });

            AddStep("ensure default profile", () => localUserManager.EnsureDefaultProfile());

            AddStep("create profile overlay", () =>
            {
                profile = new LocalUserProfileOverlay();

                Child = new DependencyProvidingContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    CachedDependencies = new (Type, object)[] { (typeof(LocalUserProfileOverlay), profile) },
                    Child = profile,
                };
            });
        }

        [Test]
        public void TestPlayerOneProfile()
        {
            AddStep("show user", () => profile.ShowUser(new APIUser { Id = 1, Username = "PlayerOne" }, ruleset.RulesetInfo));
            AddUntilStep("wait for scores to appear", () => profile.ChildrenOfType<DrawableProfileLocalScore>().Any());
            AddAssert("PlayerOne has 3 best scores", () => profile.ChildrenOfType<DrawableProfileLocalScore>().Count() >= 3);
        }

        [Test]
        public void TestPlayerTwoProfile()
        {
            AddStep("show user", () => profile.ShowUser(new APIUser { Id = 2, Username = "PlayerTwo" }, ruleset.RulesetInfo));
            AddUntilStep("wait for scores to appear", () => profile.ChildrenOfType<DrawableProfileLocalScore>().Any());
            AddAssert("PlayerTwo has 2 best scores", () => profile.ChildrenOfType<DrawableProfileLocalScore>().Count() >= 2);
        }

        [Test]
        public void TestProfileSwitching()
        {
            AddStep("show PlayerOne", () => profile.ShowUser(new APIUser { Id = 1, Username = "PlayerOne" }, ruleset.RulesetInfo));
            AddUntilStep("wait for PlayerOne to load", () => profile.ChildrenOfType<DrawableProfileLocalScore>().Any());
            AddAssert("PlayerOne has 3 scores", () => profile.ChildrenOfType<DrawableProfileLocalScore>().Count() >= 3);

            AddStep("switch to PlayerTwo", () => profile.ShowUser(new APIUser { Id = 2, Username = "PlayerTwo" }, ruleset.RulesetInfo));
            AddUntilStep("wait for PlayerTwo to load", () => profile.ChildrenOfType<DrawableProfileLocalScore>().Any());
            AddAssert("PlayerTwo has 2 scores", () => profile.ChildrenOfType<DrawableProfileLocalScore>().Count() >= 2);
        }

        private ScoreInfo createScore(Realms.Realm r, RulesetInfo rs, string username, string difficultyName, double pp, ScoreRank rank, DateTimeOffset date)
        {
            var beatmap = new BeatmapInfo
            {
                DifficultyName = difficultyName,
                Ruleset = r.Find<RulesetInfo>(rs.ShortName) ?? rs,
                Difficulty = new BeatmapDifficulty
                {
                    OverallDifficulty = 5,
                    CircleSize = 4,
                    ApproachRate = 5,
                    DrainRate = 5,
                },
                Metadata = new BeatmapMetadata
                {
                    Title = "Test Beatmap",
                    Artist = "Test Artist",
                },
            };

            var user = new RealmUser { Username = username, OnlineID = username == "PlayerOne" ? 1 : 2 };

            return new ScoreInfo(beatmap, rs, user)
            {
                TotalScore = 1000000,
                MaxCombo = 50,
                Accuracy = 0.95,
                PP = pp,
                Rank = rank,
                Date = date,
                DeletePending = false,
                Mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>(),
            };
        }
    }
}
