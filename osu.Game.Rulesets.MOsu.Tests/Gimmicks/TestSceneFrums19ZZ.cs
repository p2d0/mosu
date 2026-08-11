using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets.MOsu.Gimmicks;
using osu.Game.Rulesets.MOsu.Gimmicks;
using osu.Game.Rulesets.MOsu.Tests.Screens;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.MOsu.Tests.Gimmicks
{
    public partial class TestSceneFrums19ZZ : TestSceneOsuPlayer
    {
        protected override bool HasCustomSteps => true;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        private const string beatmap_filename = "Frums (unknown lambda) - 19ZZ (ThunderBirdo).olz";

        private Live<BeatmapSetInfo>? importedSet;

        [Test]
        public void TestGimmickMapParsing()
        {
            // The NuGet LegacyBeatmapDecoder skips the delta gimmick sections,
            // so parse the raw .osu from the archive and validate the entries.
            BeatmapSectionGimmicks sections = null!;
            BeatmapHitObjectGimmicks hitObjectGimmicks = null!;

            AddStep("parse gimmicks from archive", () =>
            {
                var fullpath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResources", beatmap_filename);
                if (!File.Exists(fullpath))
                    Assert.Fail($"Beatmap file not found at {fullpath}");

                using var archive = ZipFile.OpenRead(fullpath);
                var entry = archive.Entries.First(e => e.FullName.EndsWith(".osu", StringComparison.OrdinalIgnoreCase));

                using var stream = entry.Open();
                using var reader = new StreamReader(stream);
                (sections, hitObjectGimmicks) = MosuGimmickParser.Parse(reader);
            });

            AddAssert("no section gimmicks", () => sections.Sections.Count == 0);
            AddAssert("hitobject gimmick entries parsed", () => hitObjectGimmicks.Entries.Count > 0);

            AddAssert("first entry is difficulty override", () =>
            {
                var first = hitObjectGimmicks.Entries[0];
                return first.ObjectId == 165447
                       && first.StartTime == 15037
                       && first.ComboIndexWithOffsets == 2
                       && first.Settings.EnableDifficultyOverrides
                       && first.Settings.SectionApproachRate == 9.8f
                       && first.Settings.SectionStackLeniency == 0.2f
                       && first.Settings.SectionTickRate == 1;
            });

            AddAssert("fake notes present", () =>
            {
                var fakeNotes = hitObjectGimmicks.Entries.Where(e => e.Settings.IsFakeNote).ToList();
                Logger.Log($"[TEST] parsed {fakeNotes.Count} fake notes");

                var last = fakeNotes.LastOrDefault();
                return last != null
                       && last.Settings.FakePlayHitsound
                       && last.Settings.FakeAutoHitOnApproachClose
                       && last.Settings.EnableNoMiss;
            });
        }

        [Test]
        public void TestAutoplayWithGimmickMap()
        {
            AddStep("import beatmap", () =>
            {
                var fullpath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestResources", beatmap_filename);
                if (!File.Exists(fullpath))
                    Assert.Fail($"Beatmap file not found at {fullpath}");

                using var stream = File.OpenRead(fullpath);
                var importTask = new ImportTask(stream, beatmap_filename);
                importedSet = beatmapManager.Import(importTask).GetResultSafely();
                if (importedSet == null)
                    Assert.Fail("Failed to import beatmap");
            });

            // The player scene wraps the working beatmap in a TestWorkingBeatmap whose GetStream
            // always returns null, so the runtime parse (which reads the file) cannot run here.
            // Pre-parse the file and seed the session cache with the same key the runtime uses.
            AddStep("pre-parse gimmicks into cache", () =>
            {
                var beatmapInfo = importedSet!.PerformRead(s => s.Beatmaps.First(b => b.Ruleset.ShortName == "osu"));
                var working = beatmapManager.GetWorkingBeatmap(beatmapInfo);

                var storagePath = working.BeatmapInfo.BeatmapSet?.GetPathForFile(working.BeatmapInfo.Path);
                Assert.That(storagePath, Is.Not.Null, $"could not resolve storage path for {working.BeatmapInfo.Path}");

                using var stream = working.GetStream(storagePath!);
                Assert.That(stream, Is.Not.Null, $"could not open {storagePath}");

                using var reader = new StreamReader(stream);
                var (sections, hitObjectGimmicks) = MosuGimmickParser.Parse(reader);
                Assert.That(hitObjectGimmicks.Entries.Count, Is.GreaterThan(0));

                string key = $"{working.BeatmapInfo.OnlineID}:{working.BeatmapInfo.MD5Hash}:{working.BeatmapInfo.Path}";
                MosuGimmickCache.Set(key, new MosuGimmickData
                {
                    Sections = sections,
                    HitObjectGimmicks = hitObjectGimmicks,
                    Parsed = true,
                });

                Logger.Log($"[TEST] pre-parsed {sections.Sections.Count} sections, {hitObjectGimmicks.Entries.Count} hitobject gimmicks (key: {key})");
            });

            AddStep("load player with autoplay", () =>
            {
                var osuRuleset = new MosuRuleset();
                var beatmapInfo = importedSet!.PerformRead(s => s.Beatmaps.First(b => b.Ruleset.ShortName == "osu"));
                var working = beatmapManager.GetWorkingBeatmap(beatmapInfo);

                Ruleset.Value = osuRuleset.RulesetInfo;
                Beatmap.Value = working;
                SelectedMods.Value = new Mod[] { osuRuleset.GetAutoplayMod()! };

                Player = CreatePlayer(osuRuleset)!;
                LoadScreen(Player);
            });

            AddUntilStep("wait for player loaded", () => Player.IsLoaded && Player.Alpha == 1);
            AddUntilStep("wait for track running", () => Beatmap.Value.Track.IsRunning);

            AddStep("verify drawable beatmap", () =>
            {
                var drawableRuleset = (DrawableRuleset<OsuHitObject>)Player.DrawableRuleset;
                Logger.Log($"[TEST] drawableBeatmap type={drawableRuleset.Beatmap.GetType().Name} objectCount={drawableRuleset.Beatmap.HitObjects.Count}");
            });
        }
    }
}
