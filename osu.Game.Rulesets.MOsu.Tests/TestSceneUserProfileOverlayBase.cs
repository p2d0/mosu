using System;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Online.API.Requests.Responses;using osu.Game.Rulesets.MOsu.Models;
using osu.Game.Rulesets.MOsu.UI.LocalUser;
using osu.Game.Models;
using osu.Game.Scoring;
using osu.Game.Users;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.MOsu.Tests
{
    public abstract partial class TestSceneUserProfileOverlayBase : TestSceneMOsuBase
    {
        protected LocalUserProfileOverlay profile = null!;

        protected override Ruleset CreateRuleset() => new OsuRuleset();

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
                Realm.Write(r =>
                {
                    r.RemoveAll<ScoreInfo>();
                    r.RemoveAll<BeatmapInfo>();

                    var rs = r.Find<RulesetInfo>(ruleset.RulesetInfo.ShortName);

                    r.Add(createScore(r, rs, "PlayerOne", "Difficulty 1", 150.5, ScoreRank.S, DateTimeOffset.Now.AddDays(-1)));
                    r.Add(createScore(r, rs, "PlayerOne", "Difficulty 2", 120.3, ScoreRank.A, DateTimeOffset.Now.AddDays(-2)));
                    r.Add(createScore(r, rs, "PlayerOne", "Difficulty 3", 95.0, ScoreRank.A, DateTimeOffset.Now));
                });
            });

            AddStep("seed scores for PlayerTwo", () =>
            {
                Realm.Write(r =>
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

        private static ScoreInfo createScore(Realms.Realm r, RulesetInfo rs, string username, string difficultyName, double pp, ScoreRank rank, DateTimeOffset date)
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
                Statistics = new System.Collections.Generic.Dictionary<osu.Game.Rulesets.Scoring.HitResult, int>
                {
                    { osu.Game.Rulesets.Scoring.HitResult.Great, 45 },
                    { osu.Game.Rulesets.Scoring.HitResult.Good, 3 },
                    { osu.Game.Rulesets.Scoring.HitResult.Ok, 2 },
                    { osu.Game.Rulesets.Scoring.HitResult.Miss, 1 },
                },
            };
        }
    }
}
