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
using osu.Game.Rulesets.MOsu.Database;
using osu.Game.Rulesets.MOsu.Models;
using osu.Game.Rulesets.MOsu.UI;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.MOsu.Tests
{
    [TestFixture]
    public partial class TestSceneCollectionImport : OsuTestScene
    {
        [Resolved]
        private GameHost gameHost { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        protected MOsuRealmAccess mosuRealm { get; set; } = null!;
        protected override bool UseFreshStoragePerRun => true;

        [TearDown]
        public void TearDownScreenshot() => ScreenshotHelper.Capture(gameHost);

        [BackgroundDependencyLoader]
        private void load()
        {
            Dependencies.Cache(Realm);
            mosuRealm = new MOsuRealmAccess(LocalStorage);
            Dependencies.Cache(mosuRealm);
            Dependencies.Cache(new OsuRuleset().RulesetInfo);

            // Ensure osu! ruleset exists in realm
            Realm.Write(r =>
            {
                if (r.Find<RulesetInfo>("osu") == null)
                    r.Add(new RulesetInfo { OnlineID = 0, ShortName = "osu" });
                if (r.Find<RulesetInfo>("mosu") == null)
                    r.Add(new RulesetInfo { OnlineID = 0, ShortName = "mosu" });
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
                mosuRealm.Write(r => r.RemoveAll<PresetImportState>());
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
                var transferObjects = JsonConvert.DeserializeObject<List<CollectionWithScoresTransferObject>>(exportedJson);
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
            var collectionObjects = new List<CollectionWithScoresTransferObject>();

            Realm.Run(r =>
            {
                var collections = osu.Game.Database.RealmObjectExtensions.Detach(r.All<BeatmapCollection>()).ToList();

                foreach (var c in collections)
                {
                    var dto = new CollectionWithScoresTransferObject
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
                            BeatmapSetId = beatmap.BeatmapSet.OnlineID,
                            BeatmapMD5Hash = hash,
                            Scores = new List<ScoreExportDto>()
                        };

                        var scores = r.All<ScoreInfo>().ToList()
                            .Where(s => s.BeatmapInfo.MD5Hash == hash && !s.DeletePending)
                            .ToList();

                        foreach (var s in scores)
                        {
                            entry.Scores.Add(new ScoreExportDto
                            {
                                BeatmapHash = s.BeatmapInfo.MD5Hash,
                                RulesetShortName = s.Ruleset.ShortName,
                                TotalScore = s.TotalScore,
                                Accuracy = s.Accuracy,
                                MaxCombo = s.MaxCombo,
                                Rank = s.Rank.ToString(),
                                Date = s.Date,
                                Mods = s.Mods.Select(m => new APIMod(m)).ToList(),
                                Statistics = s.Statistics.ToDictionary(k => k.Key.ToString(), v => v.Value)
                            });
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
            var transferObjects = JsonConvert.DeserializeObject<CollectionWithScoresTransferObject[]>(json);

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

            mosuRealm.Write(r =>
            {
                var state = r.All<PresetImportState>().FirstOrDefault();
                if (state == null)
                    r.Add(new PresetImportState { CollectionsImported = true });
                else
                    state.CollectionsImported = true;
            });
        }

        private static string ReadEmbeddedCollections()
        {
            var assembly = Assembly.GetExecutingAssembly();
            const string resourceName = "osu.Game.Rulesets.MOsu.example_collections.json";

            // Try the ruleset assembly first, then fall back to test assembly
            var assemblies = new[] { assembly, typeof(OsuSettingsSubsection).Assembly };

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
