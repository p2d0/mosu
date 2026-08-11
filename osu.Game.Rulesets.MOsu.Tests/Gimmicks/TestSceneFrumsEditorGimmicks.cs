using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets;
using osu.Game.Rulesets.MOsu.Beatmaps;
using osu.Game.Database;
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

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private Storage storage { get; set; } = null!;

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

            AddAssert("editor drawable creation returns fake and hidden drawables", () =>
            {
                if (EditorBeatmap.PlayableBeatmap is not MosuBeatmap mosuBeatmap)
                    return false;

                bool sawFake = false;
                bool sawHidden = false;
                OsuHitObject? fakeSource = null;
                OsuHitObject? hiddenSource = null;

                foreach (var o in mosuBeatmap.HitObjects.OfType<OsuHitObject>())
                {
                    var d = MosuGimmickRuntime.CreateGimmickDrawableRepresentation(mosuBeatmap, o);

                    switch (d)
                    {
                        case DrawableFakeHitCircle:
                            sawFake = true;
                            fakeSource = o;
                            break;

                        case MosuDrawableHitCircle:
                            sawHidden = true;
                            hiddenSource = o;
                            break;
                    }

                    if (sawFake && sawHidden)
                        break;
                }

                Logger.Log($"[TEST] editor drawables: fake={(sawFake ? $"yes @{fakeSource?.StartTime}" : "no")} hidden={(sawHidden ? $"yes @{hiddenSource?.StartTime}" : "no")}");
                return sawFake && sawHidden;
            });


            AddAssert("unsafe CS override applied in editor", () =>
            {
                if (EditorBeatmap.PlayableBeatmap is not MosuBeatmap mosuBeatmap)
                    return false;

                var target = mosuBeatmap.HitObjects.OfType<OsuHitObject>().FirstOrDefault(o => o.StartTime == 45752);
                Logger.Log($"[TEST] editor object@45752 Scale={target?.Scale} (CS -30 unsafe -> > 1)");
                return target != null && target.Scale > 1;
            });







            AddAssert("fake note apply/clear data path", () =>
            {
                if (EditorBeatmap.PlayableBeatmap is not MosuBeatmap mosuBeatmap)
                    return false;

                var model = new HitObjectGimmickEditorModel(EditorBeatmap);
                var target = mosuBeatmap.HitObjects.OfType<OsuHitObject>().First(o => o.StartTime == 14680);

                // apply fake
                EditorBeatmap.SelectedHitObjects.Add(target);
                model.SetSelectionBoolSetting((s, v) => s.IsFakeNote = v, true);

                var fakeObj = MosuGimmickApplier.CreateFakeObject(mosuBeatmap, mosuBeatmap.Gimmicks, target);
                bool applied = fakeObj is FakeHitCircle or FakeSlider;
                int entriesAfterApply = mosuBeatmap.Gimmicks.HitObjectGimmicks.Entries.Count;

                var newEntry = mosuBeatmap.Gimmicks.HitObjectGimmicks.Entries.FirstOrDefault(e => e.StartTime == 14680);
                var resolved = MosuGimmickApplier.GetObjectSettings(mosuBeatmap, mosuBeatmap.Gimmicks, target);
                Logger.Log($"[TEST] after fake apply: created={applied}({fakeObj?.GetType().Name}) entries={entriesAfterApply} entryFake={newEntry?.Settings.IsFakeNote} combo={newEntry?.ComboIndexWithOffsets} objCombo={target.ComboIndexWithOffsets} resolvedFake={resolved?.IsFakeNote}");

                // clear fake
                model.SetSelectionBoolSetting((s, v) => s.IsFakeNote = v, false);
                bool cleared = MosuGimmickApplier.CreateFakeObject(mosuBeatmap, mosuBeatmap.Gimmicks, target) == null;
                int entriesAfterClear = mosuBeatmap.Gimmicks.HitObjectGimmicks.Entries.Count;
                Logger.Log($"[TEST] after fake clear: cleared={cleared} entries={entriesAfterClear}");

                EditorBeatmap.SelectedHitObjects.Remove(target);
                return applied && cleared && entriesAfterApply > entriesAfterClear;
            });


            AddAssert("toolbox fake note add/delete + save persistence", () =>
            {
                if (EditorBeatmap.PlayableBeatmap is not MosuBeatmap mosuBeatmap)
                    return false;

                var model = new HitObjectGimmickEditorModel(EditorBeatmap);
                var target = mosuBeatmap.HitObjects.OfType<OsuHitObject>().FirstOrDefault(o => o.StartTime == 14680);
                if (target == null)
                    return false;

                EditorBeatmap.SelectedHitObjects.Add(target);

                // add fake
                model.SetSelectionBoolSetting((s, v) => s.IsFakeNote = v, true);
                bool applied = MosuGimmickApplier.CreateFakeObject(mosuBeatmap, mosuBeatmap.Gimmicks, target) is FakeHitCircle or FakeSlider;
                int entriesAfterAdd = mosuBeatmap.Gimmicks.HitObjectGimmicks.Entries.Count;
                Logger.Log($"[TEST] fake add: applied={applied} entries={entriesAfterAdd}");

                // save + verify the fake entry for 14680 is persisted with IsFakeNote=True
                bool saved = MosuEditorSaver.Save(EditorBeatmap, realm, storage);
                bool persisted = false;

                if (saved)
                {
                    var info = realm.Run(r => r.Find<BeatmapInfo>(mosuBeatmap.BeatmapInfo.ID)?.Detach());
                    var storagePath = info?.BeatmapSet?.GetPathForFile(info.Path);
                    if (storagePath != null)
                    {
                        using var stream = storage.GetStorageForDirectory("files").GetStream(storagePath);
                        using var reader = new StreamReader(stream);
                        string text = reader.ReadToEnd();
                        persisted = text.Split('\n').Any(line => line.StartsWith("14680,") && line.Contains("IsFakeNote=True"));
                    }
                }
                Logger.Log($"[TEST] fake persisted in saved file: {persisted}");

                // delete fake
                model.SetSelectionBoolSetting((s, v) => s.IsFakeNote = v, false);
                bool deleted = MosuGimmickApplier.CreateFakeObject(mosuBeatmap, mosuBeatmap.Gimmicks, target) == null;
                int entriesAfterDelete = mosuBeatmap.Gimmicks.HitObjectGimmicks.Entries.Count;
                Logger.Log($"[TEST] fake delete: deleted={deleted} entries={entriesAfterDelete}");

                EditorBeatmap.SelectedHitObjects.Remove(target);
                return applied && entriesAfterAdd > entriesAfterDelete && deleted && persisted;
            });

            AddAssert("save writes .osu with gimmick sections", () =>
            {
                if (EditorBeatmap.PlayableBeatmap is not MosuBeatmap mosuBeatmap)
                    return false;

                if (!MosuEditorSaver.Save(EditorBeatmap, realm, storage))
                    return false;

                var info = realm.Run(r => r.Find<BeatmapInfo>(mosuBeatmap.BeatmapInfo.ID)?.Detach());
                if (info == null || info.Path == null)
                    return false;

                var storagePath = info.BeatmapSet?.GetPathForFile(info.Path);
                if (storagePath == null)
                    return false;

                using var stream = storage.GetStorageForDirectory("files").GetStream(storagePath);
                if (stream == null)
                    return false;

                using var reader = new StreamReader(stream);
                var text = reader.ReadToEnd();

                bool hasSections = text.Contains("[BeatmapSectionGimmicks]");
                bool hasHitObjectSections = text.Contains("[BeatmapHitObjectGimmicks]");
                bool hasFakeEntries = text.Contains("IsFakeNote=True");

                Logger.Log($"[TEST] saved file has sections={hasSections} hitobj={hasHitObjectSections} fakes={hasFakeEntries} len={text.Length}");
                return hasHitObjectSections && hasFakeEntries;
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
