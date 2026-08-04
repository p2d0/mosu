using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Online.Chat;
using osu.Game.Configuration;
using osu.Game.Rulesets.MOsu.Configuration;
using osu.Game.Rulesets.MOsu.Database;
using osu.Game.Rulesets.MOsu.UI;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Tests.Visual;


namespace osu.Game.Rulesets.MOsu.Tests.Database
{
    [TestFixture]
    public partial class TestSceneCollectionImport : OsuTestScene
    {
        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        protected MOsuRulesetConfigManager config { get; set; } = null!;
        protected override bool UseFreshStoragePerRun => true;

        [BackgroundDependencyLoader]
        private void load()
        {
            Dependencies.Cache(Realm);

            // MOsuRulesetConfigManager must be constructed on the update thread (it loads from the realm in its ctor).
            // Under dotnet test the game host already caches one, so reuse it when present.
            Scheduler.Add(() =>
            {
                config = (MOsuRulesetConfigManager?)Dependencies.Get(typeof(MOsuRulesetConfigManager))
                      ?? new MOsuRulesetConfigManager(new SettingsStore(Realm), new MosuRuleset().RulesetInfo);
            });
            Dependencies.Cache(new MosuRuleset().RulesetInfo);

            // Ensure osu! ruleset exists in realm
            Realm.Write(r =>
            {
                if (r.Find<RulesetInfo>("osu") == null)
                    r.Add(new RulesetInfo { OnlineID = 0, ShortName = "osu" });
                // Seed from the ruleset class so InstantiationInfo is set (needed for CreateInstance in score import).
                if (r.Find<RulesetInfo>("mosu") == null)
                    r.Add(new MosuRuleset().RulesetInfo);
            });
        }

        [SetUp]
        public void SetUp()
        {
            AddStep("clear collections and import state", () =>
            {
                Realm.Write(r =>
                {
                    r.RemoveAll<BeatmapCollection>();
                    r.RemoveAll<ScoreInfo>();
                    r.RemoveAll<BeatmapInfo>();
                    r.RemoveAll<BeatmapSetInfo>();
                });
                config.SetValue(MOsuRulesetSetting.CollectionsImported, false);
            });
        }

        [Test]
        public void TestCollectionsImported()
        {
            AddStep("import example collections", () =>
            {
                ImportCollections();
            });

            AddAssert("collections exist", () =>
                Realm.Run(r => r.All<BeatmapCollection>().Count() > 0));

            AddAssert("MOsu examples collection exists", () =>
                Realm.Run(r => r.All<BeatmapCollection>().Any(c => c.Name == "MOsu examples")));

            AddAssert("collection has hashes", () =>
            {
                var collection = Realm.Run(r => r.All<BeatmapCollection>().FirstOrDefault(c => c.Name == "MOsu examples"));
                return collection != null && collection.BeatmapMD5Hashes.Count > 0;
            });
                    }

        [Test]
        public void TestImportIdempotent()
        {
            AddStep("import example collections", () =>
            {
                ImportCollections();
            });

            int firstCount = 0;
            AddStep("record collection count", () =>
            {
                firstCount = Realm.Run(r => r.All<BeatmapCollection>().Count());
            });

            AddStep("import again", () =>
            {
                ImportCollections();
            });

            AddAssert("collection count unchanged", () =>
            {
                int secondCount = Realm.Run(r => r.All<BeatmapCollection>().Count());
                return secondCount == firstCount;
            });
                    }

        [Test]
        public void TestHashesNotDuplicated()
        {
            AddStep("import example collections", () =>
            {
                ImportCollections();
            });

            AddAssert("no duplicate hashes in collection", () =>
            {
                var collection = Realm.Run(r => r.All<BeatmapCollection>().FirstOrDefault(c => c.Name == "MOsu examples"));
                if (collection == null) return false;
                return collection.BeatmapMD5Hashes.Distinct().Count() == collection.BeatmapMD5Hashes.Count;
            });
                    }

        [Test]
        public void TestDownloadRequiresLogin()
        {
            // Verify BeatmapModelDownloader does NOT check api.IsLoggedIn.
            // It only checks api != null. This confirms the IsLoggedIn guard
            // in BackgroundCollectionImportProcessor.startBackgroundDownload() is essential.
            DummyAPIAccess dummyApi = null!;
            BeatmapModelDownloader downloader = null!;
            bool downloadBegan = false;

            AddStep("create downloader with dummy API offline", () =>
            {
                dummyApi = new DummyAPIAccess();
                dummyApi.SetState(APIState.Offline);

                downloader = new BeatmapModelDownloader(beatmapManager, dummyApi);
                downloader.DownloadBegan += _ => downloadBegan = true;
            });

            AddAssert("not logged in", () => !dummyApi.IsLoggedIn);

            AddStep("try download while offline", () =>
            {
                downloader.Download(new APIBeatmapSet { OnlineID = 99756 });
            });

            // BeatmapModelDownloader attempts download regardless of login state.
            // It only checks api != null, not api.IsLoggedIn.
            AddAssert("download attempted despite offline (proves IsLoggedIn guard needed)", () => downloadBegan);
                    }

        [Test]
        public void TestExportImportRoundTrip()
        {
            AddStep("seed beatmaps and scores", () => SeedBeatmapsAndScores());

            AddAssert("beatmaps exist", () =>
                Realm.Run(r => r.All<BeatmapSetInfo>().Count() > 0));

            AddAssert("scores exist", () =>
                Realm.Run(r => r.All<ScoreInfo>().Count() > 0));

            string exportedJson = "";
            AddStep("create collection", () =>
            {
                var hashes = Realm.Run(r => r.All<BeatmapInfo>().ToList().Select(b => b.MD5Hash).ToList());
                var collection = new BeatmapCollection("Test collection");
                foreach (var h in hashes)
                    collection.BeatmapMD5Hashes.Add(h);
                Realm.Write(r => r.Add(collection));
            });

            AddStep("export", () =>
            {
                exportedJson = RunExport();
            });

            AddAssert("export contains collection", () =>
                exportedJson.Contains("\"Test collection\""));

            AddAssert("export contains beatmap entries", () =>
                exportedJson.Contains("\"BeatmapSetId\""));

            AddAssert("export contains scores", () =>
                exportedJson.Contains("\"TotalScore\""));

            AddStep("clear collections", () =>
            {
                Realm.Write(r => r.RemoveAll<BeatmapCollection>());
            });

            AddAssert("collection removed", () =>
                Realm.Run(r => r.All<BeatmapCollection>().Count() == 0));

            AddStep("import from export", () =>
            {
                var transferObjects = JsonConvert.DeserializeObject<List<CollectionTransferObject>>(exportedJson);
                if (transferObjects == null) return;

                Realm.Write(r =>
                {
                    foreach (var dto in transferObjects)
                    {
                        var collection = r.All<BeatmapCollection>().FirstOrDefault(c => c.Name == dto.Name);
                        if (collection == null)
                        {
                            collection = new BeatmapCollection(dto.Name);
                            r.Add(collection);
                        }

                        foreach (var beatmapEntry in dto.Beatmaps)
                        {
                            if (!collection.BeatmapMD5Hashes.Contains(beatmapEntry.BeatmapMD5Hash))
                                collection.BeatmapMD5Hashes.Add(beatmapEntry.BeatmapMD5Hash);
                        }
                    }
                });
            });

            AddAssert("collection reimported", () =>
            {
                var collection = Realm.Run(r => r.All<BeatmapCollection>().FirstOrDefault(c => c.Name == "Test collection"));
                return collection != null && collection.BeatmapMD5Hashes.Count > 0;
            });

            AddAssert("hashes match original", () =>
            {
                var collection = Realm.Run(r => r.All<BeatmapCollection>().FirstOrDefault(c => c.Name == "Test collection"));
                var originalHashes = Realm.Run(r => r.All<BeatmapInfo>().ToList().Select(b => b.MD5Hash).ToHashSet());
                if (collection == null) return false;
                return collection.BeatmapMD5Hashes.OrderBy(x => x).SequenceEqual(originalHashes.OrderBy(x => x));
            });

            AddAssert("imported collection in realm", () =>
            {
                var collection = Realm.Run(r => r.All<BeatmapCollection>().FirstOrDefault(c => c.Name == "Test collection"));
                return collection != null
                    && collection.BeatmapMD5Hashes.Count == 3
                    && collection.BeatmapMD5Hashes.Contains("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")
                    && collection.BeatmapMD5Hashes.Contains("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb")
                    && collection.BeatmapMD5Hashes.Contains("cccccccccccccccccccccccccccccccc");
            });
                    }

        [Test]
        public void TestScoreExportIncludesPlayer()
        {
            AddStep("seed beatmaps and scores", () => SeedBeatmapsAndScores());

            ScoreExportDto? exported = null;
            AddStep("export score via production mapping", () =>
            {
                var score = Realm.Run(r => r.All<ScoreInfo>().FirstOrDefault(s => s.TotalScore == 100000));
                exported = ScoreExportDto.FromScore(score!);
            });

            AddAssert("player online id exported", () => exported!.UserOnlineId == 999);
            AddAssert("player username exported", () => exported!.UserUsername == "TestUser");

            AddAssert("player survives json round-trip", () =>
            {
                string json = JsonConvert.SerializeObject(exported);
                var back = JsonConvert.DeserializeObject<ScoreExportDto>(json);
                return back != null && back.UserOnlineId == 999 && back.UserUsername == "TestUser";
            });
                    }

        [Test]
        public void TestScoreImportRestoresPlayer()
        {
            AddStep("seed beatmaps and scores", () => SeedBeatmapsAndScores());

            AddStep("import collection json with score player", () =>
            {
                var dto = new CollectionTransferObject
                {
                    Name = "Player test",
                    Beatmaps = new List<CollectionBeatmapEntry>
                    {
                        new CollectionBeatmapEntry
                        {
                            BeatmapSetId = 100,
                            BeatmapMD5Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                            Scores = new List<ScoreExportDto>
                            {
                                new ScoreExportDto
                                {
                                    BeatmapHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                                    RulesetShortName = "mosu",
                                    TotalScore = 123456,
                                    Accuracy = 0.99,
                                    MaxCombo = 100,
                                    Rank = "S",
                                    Date = DateTimeOffset.UtcNow,
                                    UserOnlineId = 4242,
                                    UserUsername = "PlayerOne",
                                }
                            }
                        }
                    }
                };

                string json = JsonConvert.SerializeObject(new List<CollectionTransferObject> { dto });
                var processor = new CollectionImportProcessor(Realm, new TestNotificationOverlay(), new DummyAPIAccess(), beatmapManager, action => action());
                _ = processor.Import(json);
            });

            AddUntilStep("score imported with player", () =>
            {
                var score = Realm.Run(r => r.All<ScoreInfo>().FirstOrDefault(s => s.TotalScore == 123456));
                return score != null && score.RealmUser.OnlineID == 4242 && score.RealmUser.Username == "PlayerOne";
            });
                    }

        [Test]
        public void TestPlayCountPersistsInProfileJson()
        {
            LocalUserManager manager = null!;

            AddStep("setup manager", () =>
            {
                manager = new LocalUserManager(new MosuRuleset(), Realm, config, new DummyAPIAccess());
                manager.AddProfile("TestUser");
            });

            AddStep("seed unrelated realm scores", () => SeedBeatmapsAndScores());

            AddStep("increment play count twice", () =>
            {
                manager.IncrementPlayCount("TestUser");
                manager.IncrementPlayCount("TestUser");
            });

            AddAssert("play count persisted in profile json", () =>
                manager.GetProfiles().First(p => p.Name == "TestUser").PlayCount == 2);

            AddAssert("play count loaded from json, not realm scores", () =>
            {
                var user = manager.GetLocalUserWithStatisticsForUsernameAsync("TestUser", new MosuRuleset().RulesetInfo).GetAwaiter().GetResult();
                return user.Statistics?.PlayCount == 2;
            });

            AddAssert("incrementing unknown profile is a no-op", () =>
            {
                int before = manager.GetProfiles().Count;
                manager.IncrementPlayCount("DoesNotExist");
                return manager.GetProfiles().Count == before;
            });
                    }

        [Test]
        public void TestShouldCountPlayGate()
        {
            ScoreInfo score = null!;

            AddStep("create score with a hit", () =>
            {
                score = new ScoreInfo
                {
                    TotalScore = 1000,
                };
                score.Statistics[HitResult.Great] = 1;
            });
            AddAssert("counts with hits and score", () => LocalUserManager.ShouldCountPlay(score));

            AddStep("remove hits", () => score.Statistics.Clear());
            AddAssert("no hits -> not counted", () => !LocalUserManager.ShouldCountPlay(score));

            AddStep("add only misses", () => score.Statistics[HitResult.Miss] = 5);
            AddAssert("misses only -> not counted", () => !LocalUserManager.ShouldCountPlay(score));

            AddStep("zero score with hits", () =>
            {
                score.Statistics[HitResult.Great] = 1;
                score.TotalScore = 0;
            });
            AddAssert("zero score -> not counted", () => !LocalUserManager.ShouldCountPlay(score));

                    }

        [Test]
        public void TestChatPresetLinkParsed()
        {
            Message message = null!;

            AddStep("format message with preset link", () =>
            {
                // Same format sendCurrentMods posts: text + invisible osu://preset/ markdown link.
                message = new Message
                {
                    Content = $"is playing <MOsu!> with [\u200B](osu://preset/{Convert.ToBase64String(new byte[] { 1, 2, 3 }) })"
                };
                MessageFormatter.FormatMessage(message);

                // Log the parsed links for diagnosis.
                foreach (var link in message.Links)
                    Console.WriteLine($"link: url=[{link.Url}] action={link.Action}");
            });

            AddAssert("preset link detected in Message.Links", () =>
                message.Links.Any(l => l.Url.StartsWith("osu://preset/")));

                    }

        private void SeedBeatmapsAndScores()
        {
            var osuRuleset = Realm.Run(r => r.Find<RulesetInfo>("osu"));
            var mosuRuleset = Realm.Run(r => r.Find<RulesetInfo>("mosu"));

            if (osuRuleset == null || mosuRuleset == null) return;

            var testHashes = new[]
            {
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                "cccccccccccccccccccccccccccccccc"
            };

            var testSetIds = new[] { 100, 200, 300 };

            Realm.Write(r =>
            {
                for (int i = 0; i < testHashes.Length; i++)
                {
                    var setInfo = new BeatmapSetInfo
                    {
                        OnlineID = testSetIds[i],
                        DeletePending = false
                    };
                    r.Add(setInfo);

                    var beatmapInfo = new BeatmapInfo
                    {
                        MD5Hash = testHashes[i],
                        BeatmapSet = setInfo,
                        Ruleset = osuRuleset
                    };
                    setInfo.Beatmaps.Add(beatmapInfo);

                    // Seed a score for this beatmap
                    var score = new ScoreInfo(beatmapInfo, mosuRuleset)
                    {
                        TotalScore = 100000 + i * 10000,
                        Accuracy = 0.95,
                        MaxCombo = 50,
                        Rank = ScoreRank.A,
                        Date = DateTimeOffset.UtcNow,
                        User = new osu.Game.Online.API.Requests.Responses.APIUser { Username = "TestUser", Id = 999 }
                    };
                    score.Statistics[HitResult.Great] = 100;
                    score.StatisticsJson = JsonConvert.SerializeObject(score.Statistics);
                    r.Add(score);
                }
            });
        }

        private string RunExport()
        {
            var collectionObjects = new List<CollectionTransferObject>();

            Realm.Run(r =>
            {
                var collections = osu.Game.Database.RealmObjectExtensions.Detach(r.All<BeatmapCollection>()).ToList();

                foreach (var c in collections)
                {
                    var dto = new CollectionTransferObject
                    {
                        Name = c.Name,
                        Beatmaps = new List<CollectionBeatmapEntry>()
                    };

                    foreach (var hash in c.BeatmapMD5Hashes)
                    {
                        var beatmap = r.All<BeatmapInfo>().FirstOrDefault(b => b.MD5Hash == hash);
                        if (beatmap == null) continue;

                        var entry = new CollectionBeatmapEntry
                        {
                            BeatmapSetId = beatmap.BeatmapSet!.OnlineID!,
                            BeatmapMD5Hash = hash,
                            Scores = new List<ScoreExportDto>()
                        };

                        var scores = r.All<ScoreInfo>().ToList()
                            .Where(s => s.BeatmapInfo!.MD5Hash == hash && !s.DeletePending)
                            .ToList();

                        foreach (var s in scores)
                        {
                            entry.Scores.Add(ScoreExportDto.FromScore(s));
                        }

                        dto.Beatmaps.Add(entry);
                    }

                    collectionObjects.Add(dto);
                }
            });

            return JsonConvert.SerializeObject(collectionObjects, Formatting.Indented);
        }

        /// <summary>
        /// Import example collections directly without needing BackgroundCollectionImportProcessor's full dependencies.
        /// This tests the core data import logic (JSON parsing + Realm writes) without requiring IModelImporter.
        /// </summary>
        private void ImportCollections()
        {
            string json = ReadEmbeddedCollections();
            var transferObjects = JsonConvert.DeserializeObject<CollectionTransferObject[]>(json);

            if (transferObjects == null || transferObjects.Length == 0) return;

            Realm.Write(r =>
            {
                foreach (var dto in transferObjects)
                {
                    var collection = r.All<BeatmapCollection>().FirstOrDefault(c => c.Name == dto.Name);
                    if (collection == null)
                    {
                        collection = new BeatmapCollection(dto.Name);
                        r.Add(collection);
                    }

                    foreach (var beatmapEntry in dto.Beatmaps)
                    {
                        if (!collection.BeatmapMD5Hashes.Contains(beatmapEntry.BeatmapMD5Hash))
                            collection.BeatmapMD5Hashes.Add(beatmapEntry.BeatmapMD5Hash);
                    }
                }
            });

            config.SetValue(MOsuRulesetSetting.CollectionsImported, true);
        }

        private static string ReadEmbeddedCollections()
        {
            var assembly = Assembly.GetExecutingAssembly();
            const string resourceName = "osu.Game.Rulesets.MOsu.example_collections.json";

            // Try the ruleset assembly first, then fall back to test assembly
            var assemblies = new[] { assembly, typeof(osu.Game.Rulesets.MOsu.UI.MosuSettingsSubsection).Assembly };

            foreach (var asm in assemblies)
            {
                using (var stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (var reader = new StreamReader(stream))
                            return reader.ReadToEnd();
                    }
                }
            }

            throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");
        }
    }
}
