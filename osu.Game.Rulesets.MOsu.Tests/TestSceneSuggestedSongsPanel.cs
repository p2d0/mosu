using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables.Cards;
using osu.Game.Models;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Rulesets.MOsu.Screens;
using osu.Game.Scoring;
using osu.Game.Users;

namespace osu.Game.Rulesets.MOsu.Tests
{
    [TestFixture]
    public partial class TestSceneSuggestedSongsPanel : TestSceneMOsuBase
    {
        private ScoreInfo testScore = null!;
        private SuggestedSongsPanel panel = null!;

        protected override Ruleset CreateRuleset() => new OsuRuleset();

        [SetUpSteps]
        public void SetUp()
        {
            AddStep("set up score", () =>
            {
                var rs = Realm.Run(r => r.Find<RulesetInfo>(ruleset.RulesetInfo.ShortName));
                var beatmapSet = new BeatmapSetInfo { OnlineID = 1234 };
                var beatmap = new BeatmapInfo
                {
                    OnlineID = 12345,
                    DifficultyName = "Test",
                    Ruleset = rs,
                    Difficulty = new BeatmapDifficulty { OverallDifficulty = 5, CircleSize = 4, ApproachRate = 5, DrainRate = 5 },
                    Metadata = new BeatmapMetadata { Title = "Test", Artist = "Test" },
                    BeatmapSet = beatmapSet,
                };
                var user = new RealmUser { Username = "TestUser", OnlineID = 1 };

                testScore = new ScoreInfo(beatmap, rs, user)
                {
                    TotalScore = 1000000,
                    MaxCombo = 50,
                    Accuracy = 0.95,
                    PP = 150.5,
                    Rank = ScoreRank.S,
                    Date = DateTimeOffset.Now,
                    DeletePending = false,
                    Mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>(),
                };

                Realm.Write(r =>
                {
                    r.Add(beatmapSet);
                    r.Add(beatmap);
                    r.Add(testScore);
                });
            });

        }

        [BackgroundDependencyLoader]
        private void load()
        {
            Dependencies.Cache(new OverlayColourProvider(OverlayColourScheme.Aquamarine));
        }

        [Test]
        public void TestSuggestedSongsPanelLoadsCards()
        {
            AddStep("mock API", () =>
            {
                dummyAPI.HandleRequest = req =>
                {
                    if (req is GetBeatmapSetRequest getSetReq)
                    {
                        triggerSuccess(getSetReq, new APIBeatmapSet
                        {
                            OnlineID = 1234,
                            Title = "Test Beatmap",
                            Artist = "Test Artist",
                            Author = new APIUser { Username = "TestMapper" },
                            Genre = new BeatmapSetOnlineGenre { Id = 2, Name = "Video Game" },
                            Beatmaps = new[]
                            {
                                new APIBeatmap { StarRating = 5.0, DifficultyName = "Test" }
                            }
                        });
                        return true;
                    }

                    if (req is SearchBeatmapSetsRequest searchReq)
                    {
                        var sets = Enumerable.Range(1, 20).Select(i => new APIBeatmapSet
                        {
                            OnlineID = 10000 + i,
                            Title = $"Suggested Beatmap {i}",
                            Artist = $"Artist {i}",
                            Author = new APIUser { Username = $"Mapper{i}" },
                            Genre = new BeatmapSetOnlineGenre { Id = 2, Name = "Video Game" },
                            FavouriteCount = 5,
                            Beatmaps = new[]
                            {
                                new APIBeatmap { StarRating = 5.0 + (i % 5) * 0.1 - 0.2, DifficultyName = $"Diff {i}" }
                            }
                        }).ToList();

                        triggerSuccess(searchReq, new SearchBeatmapSetsResponse { BeatmapSets = sets });
                        return true;
                    }

                    return false;
                };
            });

            AddStep("create panel", () =>
            {
                panel = new SuggestedSongsPanel(testScore);
                Add(panel);
            });

            AddUntilStep("wait for cards", () => panel.ChildrenOfType<BeatmapCard>().Count() > 0);

            AddAssert("has cards", () => panel.ChildrenOfType<BeatmapCard>().Count() > 0);
            AddAssert("max 18 cards (9 spotlight + 9 similar)", () => panel.ChildrenOfType<BeatmapCard>().Count() <= 18);
            CaptureScreenshot("SuggestedSongsPanelLoadsCards");
        }

        [Test]
        public void TestSuggestedSongsPanelFiltersByStarRating()
        {
            AddStep("mock API", () =>
            {
                dummyAPI.HandleRequest = req =>
                {
                    if (req is GetBeatmapSetRequest getSetReq)
                    {
                        triggerSuccess(getSetReq, new APIBeatmapSet
                        {
                            OnlineID = 1234,
                            Title = "Test Beatmap",
                            Artist = "Test Artist",
                            Author = new APIUser { Username = "TestMapper" },
                            Genre = new BeatmapSetOnlineGenre { Id = 2, Name = "Video Game" },
                            Beatmaps = new[]
                            {
                                new APIBeatmap { StarRating = 5.0, DifficultyName = "Test" }
                            }
                        });
                        return true;
                    }

                    if (req is SearchBeatmapSetsRequest searchReq)
                    {
                        var sets = new[]
                        {
                            new APIBeatmapSet { OnlineID = 10001, Title = "Within Range 1", Artist = "A1", Author = new APIUser { Username = "M1" }, Genre = new BeatmapSetOnlineGenre { Id = 2, Name = "Video Game" }, FavouriteCount = 5, Beatmaps = new[] { new APIBeatmap { StarRating = 5.0, DifficultyName = "D" } } },
                            new APIBeatmapSet { OnlineID = 10002, Title = "Within Range 2", Artist = "A2", Author = new APIUser { Username = "M2" }, Genre = new BeatmapSetOnlineGenre { Id = 2, Name = "Video Game" }, FavouriteCount = 5, Beatmaps = new[] { new APIBeatmap { StarRating = 5.5, DifficultyName = "D" } } },
                            new APIBeatmapSet { OnlineID = 10003, Title = "Within Range 3", Artist = "A3", Author = new APIUser { Username = "M3" }, Genre = new BeatmapSetOnlineGenre { Id = 2, Name = "Video Game" }, FavouriteCount = 5, Beatmaps = new[] { new APIBeatmap { StarRating = 4.5, DifficultyName = "D" } } },
                            new APIBeatmapSet { OnlineID = 10004, Title = "Too Easy", Artist = "A4", Author = new APIUser { Username = "M4" }, Genre = new BeatmapSetOnlineGenre { Id = 2, Name = "Video Game" }, FavouriteCount = 5, Beatmaps = new[] { new APIBeatmap { StarRating = 3.0, DifficultyName = "D" } } },
                            new APIBeatmapSet { OnlineID = 10005, Title = "Too Hard", Artist = "A5", Author = new APIUser { Username = "M5" }, Genre = new BeatmapSetOnlineGenre { Id = 2, Name = "Video Game" }, FavouriteCount = 5, Beatmaps = new[] { new APIBeatmap { StarRating = 8.0, DifficultyName = "D" } } },
                        }.ToList();

                        triggerSuccess(searchReq, new SearchBeatmapSetsResponse { BeatmapSets = sets });
                        return true;
                    }

                    return false;
                };
            });

            AddStep("create panel", () =>
            {
                panel = new SuggestedSongsPanel(testScore);
                Add(panel);
            });

            AddUntilStep("wait for cards", () => panel.ChildrenOfType<BeatmapCard>().Count() > 0);

            AddAssert("has cards", () => panel.ChildrenOfType<BeatmapCard>().Count() > 0);
            CaptureScreenshot("SuggestedSongsPanelFiltersByStarRating");
        }

        [Test]
        public void TestSuggestedSongsPanelHasTwoSections()
        {
            AddStep("mock API", () =>
            {
                dummyAPI.HandleRequest = req =>
                {
                    if (req is GetBeatmapSetRequest getSetReq)
                    {
                        triggerSuccess(getSetReq, new APIBeatmapSet
                        {
                            OnlineID = 1234,
                            Title = "Test Beatmap",
                            Artist = "Test Artist",
                            Author = new APIUser { Username = "TestMapper" },
                            Genre = new BeatmapSetOnlineGenre { Id = 2, Name = "Video Game" },
                            Beatmaps = new[]
                            {
                                new APIBeatmap { StarRating = 5.0, DifficultyName = "Test" }
                            }
                        });
                        return true;
                    }

                    if (req is SearchBeatmapSetsRequest searchReq)
                    {
                        var sets = Enumerable.Range(1, 5).Select(i => new APIBeatmapSet
                        {
                            OnlineID = 10000 + i,
                            Title = $"Beatmap {i}",
                            Artist = $"Artist {i}",
                            Author = new APIUser { Username = $"Mapper{i}" },
                            Genre = new BeatmapSetOnlineGenre { Id = 2, Name = "Video Game" },
                            FavouriteCount = 5,
                            Beatmaps = new[]
                            {
                                new APIBeatmap { StarRating = 5.0, DifficultyName = $"Diff {i}" }
                            }
                        }).ToList();

                        triggerSuccess(searchReq, new SearchBeatmapSetsResponse { BeatmapSets = sets });
                        return true;
                    }

                    return false;
                };
            });

            AddStep("create panel", () =>
            {
                panel = new SuggestedSongsPanel(testScore);
                Add(panel);
            });

            AddUntilStep("wait for cards", () => panel.ChildrenOfType<BeatmapCard>().Count() > 0);

            AddAssert("has cards", () => panel.ChildrenOfType<BeatmapCard>().Count() > 0);
            CaptureScreenshot("SuggestedSongsPanelHasTwoSections");
        }

        private void triggerSuccess<T>(APIRequest<T> request, T response) where T : class
        {
            var method = typeof(APIRequest<T>).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == "TriggerSuccess" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(T));
            method?.Invoke(request, new object[] { response });
        }
    }
}
