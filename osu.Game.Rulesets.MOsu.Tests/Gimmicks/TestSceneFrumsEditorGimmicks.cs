using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Logging;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets;
using osu.Game.Rulesets.MOsu.Beatmaps;
using osu.Game.Rulesets.MOsu.Edit;
using osu.Game.Rulesets.MOsu.Gimmicks;
using osu.Game.Rulesets.MOsu.Objects;
using osu.Game.Rulesets.MOsu.Objects.Drawables;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.MOsu.Tests.Gimmicks
{
    public partial class TestSceneFrumsEditorGimmicks : EditorTestScene
    {
        private const string beatmap_filename = "Frums (unknown lambda) - 19ZZ (ThunderBirdo).olz";

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        private Live<BeatmapSetInfo>? importedSet;
        private WorkingBeatmap? importedWorking;

        protected override bool UseFreshStoragePerRun => true;

        protected override Ruleset CreateEditorRuleset() => new MosuRuleset();

        public override void SetUpSteps()
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

                var beatmapInfo = importedSet.PerformRead(s => s.Beatmaps.First());
                importedWorking = beatmapManager.GetWorkingBeatmap(beatmapInfo);
            });

            // The editor's working beatmap is a test wrapper with no file access, so seed the
            // gimmick cache from the real imported map (same key the runtime uses).
            AddStep("pre-parse gimmicks into cache", () =>
            {
                var storagePath = importedWorking!.BeatmapInfo.BeatmapSet?.GetPathForFile(importedWorking.BeatmapInfo.Path);
                Assert.That(storagePath, Is.Not.Null);

                using var stream = importedWorking.GetStream(storagePath!);
                Assert.That(stream, Is.Not.Null);

                using var reader = new StreamReader(stream);
                var (sections, hitObjectGimmicks) = MosuGimmickParser.Parse(reader);
                Assert.That(hitObjectGimmicks.Entries.Count, Is.GreaterThan(0));

                string key = $"{importedWorking.BeatmapInfo.OnlineID}:{importedWorking.BeatmapInfo.MD5Hash}:{importedWorking.BeatmapInfo.Path}";
                MosuGimmickCache.Set(key, new MosuGimmickData
                {
                    Sections = sections,
                    HitObjectGimmicks = hitObjectGimmicks,
                    Parsed = true,
                });
            });

            base.SetUpSteps();
        }

        protected override IBeatmap CreateBeatmap(RulesetInfo ruleset)
        {
            // The editor converts with the map's BeatmapInfo.Ruleset, and the Frums map is osu-mode,
            // so force the ruleset to mosu to exercise the mosu editor + gimmick application.
            var beatmap = importedWorking!.Beatmap;
            beatmap.BeatmapInfo.Ruleset = ruleset;
            return beatmap;
        }

        [Test]
        public void TestGimmicksAppliedInEditor()
        {
            AddUntilStep("wait for editor loaded", () => Editor?.ReadyForUse == true);

            AddAssert("composer uses mosu editor ruleset", () => Editor.ChildrenOfType<MosuEditorDrawableRuleset>().Any());

            AddUntilStep("gimmicks applied to playable beatmap", () =>
            {
                if (EditorBeatmap.PlayableBeatmap is not MosuBeatmap mosuBeatmap)
                {
                    Logger.Log($"[TEST] playable beatmap is {EditorBeatmap.PlayableBeatmap.GetType().Name}");
                    return false;
                }

                Logger.Log($"[TEST] gimmicks parsed={mosuBeatmap.Gimmicks.Parsed} applied={mosuBeatmap.Gimmicks.Applied} entries={mosuBeatmap.Gimmicks.HitObjectGimmicks.Entries.Count}");
                return mosuBeatmap.Gimmicks.Applied;
            });

            AddAssert("fake notes resolvable in editor beatmap", () =>
            {
                if (EditorBeatmap.PlayableBeatmap is not MosuBeatmap mosuBeatmap)
                    return false;

                var fake = mosuBeatmap.HitObjects.OfType<OsuHitObject>()
                                       .Select(o => MosuGimmickApplier.CreateFakeObject(mosuBeatmap, mosuBeatmap.Gimmicks, o))
                                       .FirstOrDefault(f => f is FakeHitCircle);

                Logger.Log($"[TEST] fake found: {fake != null}");
                return fake is FakeHitCircle;
            });

            AddAssert("editor drawable creation returns fake drawables", () =>
            {
                if (EditorBeatmap.PlayableBeatmap is not MosuBeatmap mosuBeatmap)
                    return false;

                var fake = mosuBeatmap.HitObjects.OfType<OsuHitObject>()
                                       .Select(o => MosuGimmickRuntime.CreateGimmickDrawableRepresentation(mosuBeatmap, o))
                                       .FirstOrDefault(d => d != null);

                Logger.Log($"[TEST] editor drawable: {fake?.GetType().Name}");
                return fake is DrawableFakeHitCircle;
            });

            AddAssert("difficulty overrides applied to editor objects", () =>
            {
                if (EditorBeatmap.PlayableBeatmap is not MosuBeatmap mosuBeatmap)
                    return false;

                var target = mosuBeatmap.HitObjects.OfType<OsuHitObject>().FirstOrDefault(o => o.StartTime == 15037);
                Logger.Log($"[TEST] object@15037 TimePreempt={target?.TimePreempt} (base AR 9.2 -> ~570, override AR 9.8 -> ~480)");
                return target != null && Math.Abs(target.TimePreempt - 480) < 2;
            });
        }
    }
}
