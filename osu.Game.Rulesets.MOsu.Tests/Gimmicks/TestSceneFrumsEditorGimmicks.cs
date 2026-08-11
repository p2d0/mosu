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

            // The editor's working beatmap resolves the imported map's file, so the runtime's
            // EnsureApplied parses the gimmick sections itself (no cache seeding needed).
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







            AddAssert("reload binds fake to the same object when two share a slot", () =>
            {
                if (EditorBeatmap.PlayableBeatmap is not MosuBeatmap sourceMosu)
                    return false;

                // Find two objects at the same (StartTime, Combo): the fake's source + a plain
                // circle moved onto its slot (same time, same combo).
                var a = sourceMosu.HitObjects.OfType<OsuHitObject>().First(o => o.StartTime == 14680);
                var aIndex = sourceMosu.HitObjects.IndexOf(a);

                var b = new HitCircle { StartTime = a.StartTime, Position = new osuTK.Vector2(a.Position.X + 40, a.Position.Y), NewCombo = a.NewCombo, ComboOffset = a.ComboOffset };
                b.ComboIndex = a.ComboIndex;
                b.ComboIndexWithOffsets = a.ComboIndexWithOffsets;
                b.IndexInCurrentCombo = a.IndexInCurrentCombo;

                // session: A is fake; B sorted immediately before A (unstable re-sort), so A's
                // saved index is aIndex + 1 and the legacy fallback would wrongly match B first.
                var session = new MosuGimmickData();
                var sessionEntry = new HitObjectGimmickEntry { ObjectId = MosuGimmickApplier.GetObjectId(a), StartTime = a.StartTime, ComboIndexWithOffsets = a.ComboIndexWithOffsets, HitObjectIndex = aIndex + 1, Settings = new HitObjectGimmickSettings { IsFakeNote = true } };
                session.HitObjectGimmicks.Entries.Add(sessionEntry);

                // serialize, then reload into a fresh beatmap with fresh object instances
                string text = MosuGimmickSerializer.Serialize(session.Sections, session.HitObjectGimmicks);
                using var reader = new StringReader(text);
                var reparsed = MosuGimmickParser.Parse(reader);

                var freshBeatmap = new MosuBeatmap { BeatmapInfo = sourceMosu.BeatmapInfo };

                OsuHitObject? freshA = null;
                OsuHitObject? freshB = null;

                // clone all source objects into fresh instances, inserting B right before A's index
                int cursor = 0;
                foreach (var src in sourceMosu.HitObjects.OfType<OsuHitObject>())
                {
                    if (cursor == aIndex)
                    {
                        freshB = cloneObject(b);
                        freshBeatmap.HitObjects.Add(freshB);
                        cursor++;
                    }

                    var clone = cloneObject(src);
                    if (ReferenceEquals(src, a))
                        freshA = clone;

                    freshBeatmap.HitObjects.Add(clone);
                    cursor++;
                }

                if (freshA == null || freshB == null)
                    return false;

                freshBeatmap.Gimmicks.HitObjectGimmicks = reparsed.HitObjects;
                freshBeatmap.Gimmicks.Applied = false;

                MosuGimmickApplier.Apply(freshBeatmap, freshBeatmap.Gimmicks);

                bool aFake = MosuGimmickApplier.GetObjectSettings(freshBeatmap, freshBeatmap.Gimmicks, freshA)?.IsFakeNote == true;
                bool bFake = MosuGimmickApplier.GetObjectSettings(freshBeatmap, freshBeatmap.Gimmicks, freshB)?.IsFakeNote == true;

                Logger.Log($"[TEST] reload: aFake={aFake} bFake={bFake} aIndex={aIndex} entryIndex={freshBeatmap.Gimmicks.HitObjectGimmicks.Entries.FirstOrDefault()?.HitObjectIndex} aId={MosuGimmickApplier.GetObjectId(freshA)} bId={MosuGimmickApplier.GetObjectId(freshB)}");

                return aFake && !bFake;
            });

            static OsuHitObject cloneObject(OsuHitObject src)
            {
                var clone = new HitCircle { StartTime = src.StartTime, Position = src.Position, NewCombo = src.NewCombo, ComboOffset = src.ComboOffset };
                clone.ComboIndex = src.ComboIndex;
                clone.ComboIndexWithOffsets = src.ComboIndexWithOffsets;
                clone.IndexInCurrentCombo = src.IndexInCurrentCombo;
                return clone;
            }

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

                // the editor selects the drawable's HitObject (the fake clone), not the source
                bool cloneResolves = fakeObj != null
                    && MosuGimmickApplier.GetObjectSettings(mosuBeatmap, mosuBeatmap.Gimmicks, fakeObj)?.IsFakeNote == true;

                var newEntry = mosuBeatmap.Gimmicks.HitObjectGimmicks.Entries.FirstOrDefault(e => e.StartTime == 14680);
                var resolved = MosuGimmickApplier.GetObjectSettings(mosuBeatmap, mosuBeatmap.Gimmicks, target);
                Logger.Log($"[TEST] after fake apply: created={applied}({fakeObj?.GetType().Name}) cloneResolves={cloneResolves} entries={entriesAfterApply} entryFake={newEntry?.Settings.IsFakeNote} combo={newEntry?.ComboIndexWithOffsets} objCombo={target.ComboIndexWithOffsets} resolvedFake={resolved?.IsFakeNote}");

                // clear fake
                model.SetSelectionBoolSetting((s, v) => s.IsFakeNote = v, false);
                bool cleared = MosuGimmickApplier.CreateFakeObject(mosuBeatmap, mosuBeatmap.Gimmicks, target) == null;
                int entriesAfterClear = mosuBeatmap.Gimmicks.HitObjectGimmicks.Entries.Count;
                Logger.Log($"[TEST] after fake clear: cleared={cleared} entries={entriesAfterClear}");

                EditorBeatmap.SelectedHitObjects.Remove(target);
                return applied && cloneResolves && cleared && entriesAfterApply > entriesAfterClear;
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

            AddAssert("toggle ON from a cleared entry sticks", () =>
            {
                if (EditorBeatmap.PlayableBeatmap is not MosuBeatmap mosuBeatmap)
                    return false;

                var model = new HitObjectGimmickEditorModel(EditorBeatmap);
                var target = mosuBeatmap.HitObjects.OfType<OsuHitObject>().First(o => o.StartTime == 15037);

                // simulate the user's corrupted state: the entry for this object has IsFakeNote=false
                var existing = mosuBeatmap.Gimmicks.HitObjectGimmicks.Entries.FirstOrDefault(e => e.StartTime == 15037);
                if (existing == null)
                    return false;

                existing.Settings.IsFakeNote = false;

                EditorBeatmap.SelectedHitObjects.Add(target);
                model.SetSelectionBoolSetting((s, v) => s.IsFakeNote = v, true);

                var after = mosuBeatmap.Gimmicks.HitObjectGimmicks.Entries.FirstOrDefault(e => e.StartTime == 15037);
                var resolved = MosuGimmickApplier.GetObjectSettings(mosuBeatmap, mosuBeatmap.Gimmicks, target);

                Logger.Log($"[TEST] cleared-entry toggle ON: entryFake={after?.Settings.IsFakeNote} resolvedFake={resolved?.IsFakeNote} entryObjectId={after?.ObjectId} objId={MosuGimmickApplier.GetObjectId(target)}");

                EditorBeatmap.SelectedHitObjects.Remove(target);
                return after?.Settings.IsFakeNote == true && resolved?.IsFakeNote == true;
            });

            AddAssert("save writes .osu with gimmick sections", () =>
            {
                if (EditorBeatmap.PlayableBeatmap is not MosuBeatmap mosuBeatmap)
                    return false;

                var model = new HitObjectGimmickEditorModel(EditorBeatmap);
                var target = mosuBeatmap.HitObjects.OfType<OsuHitObject>().First(o => o.StartTime == 14680);
                EditorBeatmap.SelectedHitObjects.Add(target);

                // ensure a fake is active at 14680 so the save round-trip has something to reload
                if (MosuGimmickApplier.GetObjectSettings(mosuBeatmap, mosuBeatmap.Gimmicks, target)?.IsFakeNote != true)
                    model.SetSelectionBoolSetting((s, v) => s.IsFakeNote = v, true);

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

                // Full reload round-trip: parse the saved file into a fresh beatmap with fresh
                // object instances and verify the fake resolves (the editor re-enter path).
                using var parseReader = new StringReader(text);
                var reparsed = MosuGimmickParser.Parse(parseReader);

                var freshBeatmap = new MosuBeatmap { BeatmapInfo = mosuBeatmap.BeatmapInfo };

                OsuHitObject? freshTarget = null;

                foreach (var src in mosuBeatmap.HitObjects.OfType<OsuHitObject>())
                {
                    var clone = new HitCircle { StartTime = src.StartTime, Position = src.Position, NewCombo = src.NewCombo, ComboOffset = src.ComboOffset };
                    clone.ComboIndex = src.ComboIndex;
                    clone.ComboIndexWithOffsets = src.ComboIndexWithOffsets;
                    clone.IndexInCurrentCombo = src.IndexInCurrentCombo;

                    if (src.StartTime == 14680)
                        freshTarget = clone;

                    freshBeatmap.HitObjects.Add(clone);
                }

                freshBeatmap.Gimmicks.HitObjectGimmicks = reparsed.HitObjects;
                freshBeatmap.Gimmicks.Applied = false;
                MosuGimmickApplier.Apply(freshBeatmap, freshBeatmap.Gimmicks);

                bool reloadedFake = freshTarget != null
                    && MosuGimmickApplier.GetObjectSettings(freshBeatmap, freshBeatmap.Gimmicks, freshTarget)?.IsFakeNote == true;

                Logger.Log($"[TEST] saved-file reload: fake@14680 resolves={reloadedFake}");

                return hasHitObjectSections && hasFakeEntries && reloadedFake;
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
