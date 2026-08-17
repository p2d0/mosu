using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets.MOsu.Delta.Beatmaps;
using osu.Game.Rulesets.MOsu.Delta.Gimmicks;
using osu.Game.Rulesets.MOsu.Delta.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.MOsu.Tests.Delta.Gimmicks
{
    public partial class TestSceneFrumsGimmickApply : TestSceneMOsuBase
    {
        private const string beatmap_filename = "Frums (unknown lambda) - 19ZZ (ThunderBirdo).olz";

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        private Live<BeatmapSetInfo>? importedSet;
        private WorkingBeatmap working = null!;
        private DeltaBeatmap playable = null!;
        private DeltaGimmickData data = null!;

        [Test]
        public void TestGimmickApply()
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

            AddStep("convert playable beatmap", () =>
            {
                var beatmapInfo = importedSet!.PerformRead(s => s.Beatmaps.First(b => b.Ruleset.ShortName == "osu"));
                working = beatmapManager.GetWorkingBeatmap(beatmapInfo);

                playable = (DeltaBeatmap)working.GetPlayableBeatmap(new MosuRuleset().RulesetInfo, Array.Empty<Mod>());
            });

            AddStep("parse gimmicks from file", () =>
            {
                var storagePath = working.BeatmapInfo.BeatmapSet?.GetPathForFile(working.BeatmapInfo.Path);
                if (storagePath == null)
                    Assert.Fail($"Could not resolve storage path for {working.BeatmapInfo.Path}");

                using var stream = working.GetStream(storagePath);
                if (stream == null)
                    Assert.Fail($"Could not read .osu file (storage path: {storagePath})");

                using var reader = new StreamReader(stream);
                data = playable.Gimmicks;
                (data.Sections, data.HitObjectGimmicks) = DeltaGimmickParser.Parse(reader);
            });

            AddStep("apply gimmicks", () => DeltaGimmickApplier.Apply(playable, data));

            AddAssert("playable beatmap is DeltaBeatmap", () => playable is DeltaBeatmap);

            AddAssert("hitobject gimmick entries parsed", () => data.HitObjectGimmicks.Entries.Count > 0);

            AddAssert("fake notes create FakeHitCircle", () =>
            {
                var firstFake = playable.HitObjects.OfType<OsuHitObject>().FirstOrDefault(o => DeltaGimmickApplier.CreateFakeObject(playable, data, o) != null);
                if (firstFake == null)
                    return false;

                var fake = DeltaGimmickApplier.CreateFakeObject(playable, data, firstFake);
                Logger.Log($"[TEST] fake from {firstFake.GetType().Name}@{(long)firstFake.StartTime} -> {fake!.GetType().Name}");
                return fake is FakeHitCircle && ((FakeHitCircle)fake).FakeAutoHitOnApproachClose && ((FakeHitCircle)fake).FakePlayHitsound;
            });

            AddAssert("non-fake object produces no fake", () =>
            {
                var normal = playable.HitObjects.OfType<OsuHitObject>().First(o => DeltaGimmickApplier.CreateFakeObject(playable, data, o) == null);
                return normal != null;
            });

            AddAssert("first entry object AR overridden to 9.8", () =>
            {
                var target = playable.HitObjects.OfType<OsuHitObject>().FirstOrDefault(o => o.StartTime == 15037 && o.ComboIndexWithOffsets == 2);
                if (target == null)
                    return false;

                // base AR 9.2 -> preempt 570; override AR 9.8 -> preempt ~480
                Logger.Log($"[TEST] object@15037 TimePreempt={target.TimePreempt} (expected ~480)");
                return Math.Abs(target.TimePreempt - 480) < 2;
            });

            AddAssert("non-overridden object keeps base AR", () =>
            {
                var target = playable.HitObjects.OfType<OsuHitObject>().FirstOrDefault(o => o.StartTime == 14680);
                if (target == null)
                    return false;

                Logger.Log($"[TEST] object@14680 TimePreempt={target.TimePreempt} (expected ~570)");
                return Math.Abs(target.TimePreempt - 570) < 2;
            });

            AddAssert("unsafe CS override applied (-30)", () =>
            {
                if (playable.HitObjects.OfType<OsuHitObject>().FirstOrDefault(o => o.StartTime == 45752) is not OsuHitObject target)
                    return false;

                // base CS 4.1 -> scale ~0.8; CS -30 (unsafe) -> much larger scale
                Logger.Log($"[TEST] object@45752 Scale={target.Scale} (CS -30 unsafe -> scale > 1)");
                return target.Scale > 1;
            });
        }
    }
}
