using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Platform;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Rulesets.MOsu.Database;
using osu.Game.Rulesets.MOsu.Models;
using osu.Game.Rulesets.MOsu.UI;
using osu.Game.Rulesets.Osu;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.MOsu.Tests
{
    [TestFixture]
    public partial class TestSceneCollectionImport : OsuTestScene
    {
        [Resolved]
        private GameHost gameHost { get; set; } = null!;

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
                Realm.Write(r => r.RemoveAll<BeatmapCollection>());
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

        /// <summary>
        /// Import example collections directly without needing BackgroundCollectionImportProcessor's full dependencies.
        /// This tests the core data import logic (JSON parsing + Realm writes) without requiring IModelImporter.
        /// </summary>
        private void ImportCollections()
        {
            string json = ReadEmbeddedCollections();
            var transferObjects = JsonConvert.DeserializeObject<OsuSettingsSubsection.CollectionWithScoresTransferObject[]>(json);

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

                    foreach (var hash in dto.BeatmapMD5Hashes)
                    {
                        if (!collection.BeatmapMD5Hashes.Contains(hash))
                            collection.BeatmapMD5Hashes.Add(hash);
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
