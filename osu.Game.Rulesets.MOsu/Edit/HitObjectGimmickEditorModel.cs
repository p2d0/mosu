// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Logging;
using osu.Game.Rulesets.MOsu.Beatmaps;
using osu.Game.Rulesets.MOsu.Gimmicks;
using osu.Game.Rulesets.MOsu.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Edit;

namespace osu.Game.Rulesets.MOsu.Edit
{
    public class HitObjectGimmickEditorModel
    {
        public readonly struct SelectionState
        {
            public readonly bool HasSelection;
            public readonly int SelectionCount;
            public readonly bool EnableHPGimmick;
            public readonly bool IsFakeNote;
            public readonly FakePunishMode FakePunishMode;
            public readonly bool FakeAutoHitOnApproachClose;
            public readonly bool FakeAutoHitPlayHitsound;
            public readonly bool EnableNoMiss;
            public readonly bool EnableCountLimits;
            public readonly bool EnableGreatOffsetPenalty;
            public readonly bool EnableDifficultyOverrides;
            public readonly bool AllowUnsafeDifficultyOverrideValues;
            public readonly bool ForceHidden;
            public readonly bool ForceHardRock;
            public readonly bool ForceFlashlight;
            public readonly bool ForceNoApproachCircle;
            public readonly HitObjectGimmickSettings? RepresentativeSettings;

            public SelectionState(
                bool hasSelection,
                int selectionCount,
                bool enableHpGimmick,
                bool isFakeNote,
                FakePunishMode fakePunishMode,
                bool fakeAutoHitOnApproachClose,
                bool fakeAutoHitPlayHitsound,
                bool enableNoMiss,
                bool enableCountLimits,
                bool enableGreatOffsetPenalty,
                bool enableDifficultyOverrides,
                bool allowUnsafeDifficultyOverrideValues,
                bool forceHidden,
                bool forceHardRock,
                bool forceFlashlight,
                bool forceNoApproachCircle,
                HitObjectGimmickSettings? representativeSettings)
            {
                HasSelection = hasSelection;
                SelectionCount = selectionCount;
                EnableHPGimmick = enableHpGimmick;
                IsFakeNote = isFakeNote;
                FakePunishMode = fakePunishMode;
                FakeAutoHitOnApproachClose = fakeAutoHitOnApproachClose;
                FakeAutoHitPlayHitsound = fakeAutoHitPlayHitsound;
                EnableNoMiss = enableNoMiss;
                EnableCountLimits = enableCountLimits;
                EnableGreatOffsetPenalty = enableGreatOffsetPenalty;
                EnableDifficultyOverrides = enableDifficultyOverrides;
                AllowUnsafeDifficultyOverrideValues = allowUnsafeDifficultyOverrideValues;
                ForceHidden = forceHidden;
                ForceHardRock = forceHardRock;
                ForceFlashlight = forceFlashlight;
                ForceNoApproachCircle = forceNoApproachCircle;
                RepresentativeSettings = representativeSettings;
            }
        }

        private readonly EditorBeatmap editorBeatmap;
        private readonly MosuBeatmap playableBeatmap;

        /// <summary>
        /// Fired after a mutation has been written and re-applied to the playable beatmap.
        /// </summary>
        public event Action? Changed;

        private BeatmapHitObjectGimmicks gimmicks => playableBeatmap.Gimmicks.HitObjectGimmicks;

        public HitObjectGimmickEditorModel(EditorBeatmap editorBeatmap)
        {
            this.editorBeatmap = editorBeatmap;
            playableBeatmap = editorBeatmap.PlayableBeatmap as MosuBeatmap ?? throw new InvalidOperationException("Mosu editor requires a MosuBeatmap playable beatmap.");

            // When a hitobject is deleted, remove its gimmick entries so they don't get
            // rebound onto other objects by the legacy-key re-sync.
            editorBeatmap.HitObjectRemoved += onHitObjectRemoved;
        }

        private void onHitObjectRemoved(HitObject hitObject)
        {
            if (hitObject is not OsuHitObject osuHitObject)
                return;

            int before = playableBeatmap.Gimmicks.HitObjectGimmicks.Entries.Count;

            long objectId = MosuGimmickApplier.GetObjectId(osuHitObject);

            playableBeatmap.Gimmicks.HitObjectGimmicks.Entries.RemoveAll(e => e.ObjectId == objectId);

            if (playableBeatmap.Gimmicks.HitObjectGimmicks.Entries.Count != before)
            {
                osu.Framework.Logging.Logger.Log($"[MOsu-Model] removed {before - playableBeatmap.Gimmicks.HitObjectGimmicks.Entries.Count} gimmick entries for deleted object@{osuHitObject.StartTime}");
                playableBeatmap.Gimmicks.Applied = false;
                MosuGimmickApplier.Apply(playableBeatmap, playableBeatmap.Gimmicks);
                Changed?.Invoke();
            }
        }

        public bool HasSelection => editorBeatmap.SelectedHitObjects.OfType<OsuHitObject>().Any();

        public bool IsSelectionNoApproachCircleForced
        {
            get
            {
                var selected = editorBeatmap.SelectedHitObjects.OfType<OsuHitObject>().ToList();
    
                if (selected.Count == 0)
                    return false;

                var lookup = createLookup(gimmicks);
                return selected.All(h => isNoApproachForced(h, lookup));
            }
        }

        public bool IsSelectionForceNoApproachCircle
            => getSelectionBoolState(s => s.ForceNoApproachCircle);

        public bool IsSelectionAllowUnsafeDifficultyOverrideValues
            => getSelectionBoolState(s => s.AllowUnsafeDifficultyOverrideValues);

        public bool IsSelectionAllowUnsafeStackLeniencyOverrideValues
            => getSelectionBoolState(s => s.AllowUnsafeStackLeniencyOverrideValues);

        public bool IsSelectionAllowUnsafeTickRateOverrideValues
            => getSelectionBoolState(s => s.AllowUnsafeTickRateOverrideValues);

        public bool IsSelectionEnableHPGimmick
            => getSelectionBoolState(s => s.EnableHPGimmick);

        public bool IsSelectionFakeNote
            => getSelectionBoolState(s => s.IsFakeNote);

        public bool IsSelectionFakePunishAsMiss
            => getSelectionBoolState(s => s.FakePunishMode == FakePunishMode.Miss);

        public bool IsSelectionFakePlayHitsound
            => getSelectionBoolState(s => s.FakePlayHitsound);

        public bool IsSelectionFakeAutoHitOnApproachClose
            => getSelectionBoolState(s => s.FakeAutoHitOnApproachClose);

        public bool IsSelectionFakeAutoHitPlayHitsound
            => getSelectionBoolState(s => s.FakeAutoHitPlayHitsound);

        public bool IsSelectionFakeRevealEnabled
            => getSelectionBoolState(s => s.FakeRevealEnabled);

        public FakePunishMode SelectionFakePunishMode
        {
            get
            {
                var selected = editorBeatmap.SelectedHitObjects.OfType<OsuHitObject>().ToList();
    
                if (selected.Count == 0)
                    return FakePunishMode.None;

                var lookup = createLookup(gimmicks);

                if (!tryGetSettings(selected[0], lookup, out var firstSettings))
                    return FakePunishMode.None;

                return firstSettings.FakePunishMode;
            }
        }

        public bool IsSelectionEnableNoMiss
            => getSelectionBoolState(s => s.EnableNoMiss);

        public bool IsSelectionEnableCountLimits
            => getSelectionBoolState(s => s.EnableCountLimits);

        public bool IsSelectionEnableGreatOffsetPenalty
            => getSelectionBoolState(s => s.EnableGreatOffsetPenalty);

        public bool IsSelectionEnableDifficultyOverrides
            => getSelectionBoolState(s => s.EnableDifficultyOverrides);

        public bool IsSelectionForceHidden
            => getSelectionBoolState(s => s.ForceHidden);

        public bool IsSelectionForceHardRock
            => getSelectionBoolState(s => s.ForceHardRock);

        public bool IsSelectionForceFlashlight
            => getSelectionBoolState(s => s.ForceFlashlight);

        public SelectionState GetSelectionState()
        {
            var selected = editorBeatmap.SelectedHitObjects.OfType<OsuHitObject>().ToList();

            if (selected.Count == 0)
                return new SelectionState(false, 0, false, false, FakePunishMode.None, false, false, false, false, false, false, false, false, false, false, false, null);

            var lookup = createLookup(gimmicks);

            bool getBoolState(System.Func<HitObjectGimmickSettings, bool> getter)
                => selected.All(h => tryGetSettings(h, lookup, out var settings) && getter(settings));

            var first = selected[0];
            HitObjectGimmickSettings? representative = tryGetSettings(first, lookup, out var firstSettings)
                ? HitObjectGimmickBindingUtils.CloneSettings(firstSettings)
                : new HitObjectGimmickSettings();

            return new SelectionState(
                hasSelection: true,
                selectionCount: selected.Count,
                enableHpGimmick: getBoolState(s => s.EnableHPGimmick),
                isFakeNote: getBoolState(s => s.IsFakeNote),
                fakePunishMode: SelectionFakePunishMode,
                fakeAutoHitOnApproachClose: getBoolState(s => s.FakeAutoHitOnApproachClose),
                fakeAutoHitPlayHitsound: getBoolState(s => s.FakeAutoHitPlayHitsound),
                enableNoMiss: getBoolState(s => s.EnableNoMiss),
                enableCountLimits: getBoolState(s => s.EnableCountLimits),
                enableGreatOffsetPenalty: getBoolState(s => s.EnableGreatOffsetPenalty),
                enableDifficultyOverrides: getBoolState(s => s.EnableDifficultyOverrides),
                allowUnsafeDifficultyOverrideValues: getBoolState(s => s.AllowUnsafeDifficultyOverrideValues),
                forceHidden: getBoolState(s => s.ForceHidden),
                forceHardRock: getBoolState(s => s.ForceHardRock),
                forceFlashlight: getBoolState(s => s.ForceFlashlight),
                forceNoApproachCircle: selected.All(h => isNoApproachForced(h, lookup)),
                representativeSettings: representative);
        }

        public HitObjectGimmickSettings? GetSelectionRepresentativeSettings()
        {
            var selected = editorBeatmap.SelectedHitObjects.OfType<OsuHitObject>().ToList();

            if (selected.Count == 0)
                return null;

            var lookup = createLookup(gimmicks);
            var first = selected[0];

            if (!tryGetSettings(first, lookup, out var firstSettings))
                return new HitObjectGimmickSettings();

            return HitObjectGimmickBindingUtils.CloneSettings(firstSettings);
        }

        public void SetSelectionForceNoApproachCircle(bool enabled)
        {
            var selected = editorBeatmap.SelectedHitObjects.OfType<OsuHitObject>().ToList();

            if (selected.Count == 0)
                return;

            var updated = CloneHitObjectGimmicks(gimmicks);

            foreach (var hitObject in selected)
                applyOrRemoveEntry(updated, hitObject, enabled);

            editorBeatmap.BeginChange();
            playableBeatmap.Gimmicks.HitObjectGimmicks = updated;
            playableBeatmap.Gimmicks.Applied = false;
            MosuGimmickApplier.Apply(playableBeatmap, playableBeatmap.Gimmicks);
            editorBeatmap.EndChange();

            osu.Framework.Logging.Logger.Log($"[MOsu-Model] committed {updated.Entries.Count} entries, firing Changed");
            Changed?.Invoke();
        }

        public void SetSelectionBoolSetting(System.Action<HitObjectGimmickSettings, bool> setter, bool enabled)
        {
            var selected = editorBeatmap.SelectedHitObjects.OfType<OsuHitObject>().ToList();

            if (selected.Count == 0)
                return;

            var updated = CloneHitObjectGimmicks(gimmicks);

            foreach (var hitObject in selected)
            {
                long objectId = MosuGimmickApplier.GetObjectId(hitObject);
                var byId = updated.Entries.LastOrDefault(e => e.ObjectId == objectId);

                var entry = getOrCreateEntry(updated, hitObject);
                bool beforeFake = entry.Settings.IsFakeNote;
                setter(entry.Settings, enabled);
                cleanupEntryIfEmpty(updated, entry);
            }

            editorBeatmap.BeginChange();
            playableBeatmap.Gimmicks.HitObjectGimmicks = updated;
            playableBeatmap.Gimmicks.Applied = false;
            MosuGimmickApplier.Apply(playableBeatmap, playableBeatmap.Gimmicks);
            editorBeatmap.EndChange();

            osu.Framework.Logging.Logger.Log($"[MOsu-Model] committed {updated.Entries.Count} entries, firing Changed");
            Changed?.Invoke();
        }

        public void SetSelectionFakePunishMode(FakePunishMode mode)
        {
            var selected = editorBeatmap.SelectedHitObjects.OfType<OsuHitObject>().ToList();

            if (selected.Count == 0)
                return;

            var updated = CloneHitObjectGimmicks(gimmicks);

            foreach (var hitObject in selected)
            {
                var entry = getOrCreateEntry(updated, hitObject);
                entry.Settings.FakePunishMode = mode;
                cleanupEntryIfEmpty(updated, entry);
            }

            editorBeatmap.BeginChange();
            playableBeatmap.Gimmicks.HitObjectGimmicks = updated;
            playableBeatmap.Gimmicks.Applied = false;
            MosuGimmickApplier.Apply(playableBeatmap, playableBeatmap.Gimmicks);
            editorBeatmap.EndChange();

            osu.Framework.Logging.Logger.Log($"[MOsu-Model] committed {updated.Entries.Count} entries, firing Changed");
            Changed?.Invoke();
        }

        public void SetSelectionFloatSetting(System.Action<HitObjectGimmickSettings, float> setter, float value)
        {
            var selected = editorBeatmap.SelectedHitObjects.OfType<OsuHitObject>().ToList();

            if (selected.Count == 0)
                return;

            var updated = CloneHitObjectGimmicks(gimmicks);

            foreach (var hitObject in selected)
            {
                var entry = getOrCreateEntry(updated, hitObject);
                setter(entry.Settings, value);
                SectionGimmickValueClamper.ClampHitObjectSettingsInPlace(entry.Settings);
                cleanupEntryIfEmpty(updated, entry);
            }

            editorBeatmap.BeginChange();
            playableBeatmap.Gimmicks.HitObjectGimmicks = updated;
            playableBeatmap.Gimmicks.Applied = false;
            MosuGimmickApplier.Apply(playableBeatmap, playableBeatmap.Gimmicks);
            editorBeatmap.EndChange();

            osu.Framework.Logging.Logger.Log($"[MOsu-Model] committed {updated.Entries.Count} entries, firing Changed");
            Changed?.Invoke();
        }

        public void SetSelectionIntSetting(System.Action<HitObjectGimmickSettings, int> setter, int value)
        {
            var selected = editorBeatmap.SelectedHitObjects.OfType<OsuHitObject>().ToList();

            if (selected.Count == 0)
                return;

            var updated = CloneHitObjectGimmicks(gimmicks);

            foreach (var hitObject in selected)
            {
                var entry = getOrCreateEntry(updated, hitObject);
                setter(entry.Settings, value);
                SectionGimmickValueClamper.ClampHitObjectSettingsInPlace(entry.Settings);
                cleanupEntryIfEmpty(updated, entry);
            }

            editorBeatmap.BeginChange();
            playableBeatmap.Gimmicks.HitObjectGimmicks = updated;
            playableBeatmap.Gimmicks.Applied = false;
            MosuGimmickApplier.Apply(playableBeatmap, playableBeatmap.Gimmicks);
            editorBeatmap.EndChange();

            osu.Framework.Logging.Logger.Log($"[MOsu-Model] committed {updated.Entries.Count} entries, firing Changed");
            Changed?.Invoke();
        }

        public void SetSelectionDoubleSetting(System.Action<HitObjectGimmickSettings, double> setter, double value)
        {
            var selected = editorBeatmap.SelectedHitObjects.OfType<OsuHitObject>().ToList();

            if (selected.Count == 0)
                return;

            var updated = CloneHitObjectGimmicks(gimmicks);

            foreach (var hitObject in selected)
            {
                var entry = getOrCreateEntry(updated, hitObject);
                setter(entry.Settings, value);
                SectionGimmickValueClamper.ClampHitObjectSettingsInPlace(entry.Settings);
                cleanupEntryIfEmpty(updated, entry);
            }

            editorBeatmap.BeginChange();
            playableBeatmap.Gimmicks.HitObjectGimmicks = updated;
            playableBeatmap.Gimmicks.Applied = false;
            MosuGimmickApplier.Apply(playableBeatmap, playableBeatmap.Gimmicks);
            editorBeatmap.EndChange();

            osu.Framework.Logging.Logger.Log($"[MOsu-Model] committed {updated.Entries.Count} entries, firing Changed");
            Changed?.Invoke();
        }

        public static BeatmapHitObjectGimmicks CloneHitObjectGimmicks(BeatmapHitObjectGimmicks source)
            => new BeatmapHitObjectGimmicks
            {
                Entries = source.Entries.Select(e =>
                {
                    var settings = e.Settings ?? new HitObjectGimmickSettings();

                    return new HitObjectGimmickEntry
                    {
                        ObjectId = e.ObjectId,
                        StartTime = e.StartTime,
                        ComboIndexWithOffsets = e.ComboIndexWithOffsets,
                        HitObjectIndex = e.HitObjectIndex,
                        Settings = new HitObjectGimmickSettings
                        {
                            IsFakeNote = settings.IsFakeNote,
                            FakePunishMode = settings.FakePunishMode,
                            FakePlayHitsound = settings.FakePlayHitsound,
                            FakeAutoHitOnApproachClose = settings.FakeAutoHitOnApproachClose,
                            FakeAutoHitPlayHitsound = settings.FakeAutoHitPlayHitsound,
                            FakeRevealEnabled = settings.FakeRevealEnabled,
                            FakeRevealRed = settings.FakeRevealRed,
                            FakeRevealGreen = settings.FakeRevealGreen,
                            FakeRevealBlue = settings.FakeRevealBlue,
                            FakeRevealStrength = settings.FakeRevealStrength,
                            FakeRevealLeadInStartMs = settings.FakeRevealLeadInStartMs,
                            FakeRevealLeadInLengthMs = settings.FakeRevealLeadInLengthMs,
                            FakeRevealFadeOutStartMs = settings.FakeRevealFadeOutStartMs,
                            FakeRevealFadeOutLengthMs = settings.FakeRevealFadeOutLengthMs,

                            EnableHPGimmick = settings.EnableHPGimmick,
                            EnableNoMiss = settings.EnableNoMiss,
                            EnableCountLimits = settings.EnableCountLimits,
                            EnableGreatOffsetPenalty = settings.EnableGreatOffsetPenalty,
                            Max300s = settings.Max300s,
                            Max100s = settings.Max100s,
                            Max50s = settings.Max50s,
                            MaxMisses = settings.MaxMisses,
                            HP300 = settings.HP300,
                            HP100 = settings.HP100,
                            HP50 = settings.HP50,
                            HPMiss = settings.HPMiss,
                            GreatOffsetThresholdMs = settings.GreatOffsetThresholdMs,
                            GreatOffsetPenaltyHP = settings.GreatOffsetPenaltyHP,
                            EnableDifficultyOverrides = settings.EnableDifficultyOverrides,
                            AllowUnsafeDifficultyOverrideValues = settings.AllowUnsafeDifficultyOverrideValues,
                            SectionCircleSize = settings.SectionCircleSize,
                            SectionApproachRate = settings.SectionApproachRate,
                            SectionOverallDifficulty = settings.SectionOverallDifficulty,
                            AllowUnsafeStackLeniencyOverrideValues = settings.AllowUnsafeStackLeniencyOverrideValues,
                            SectionStackLeniency = settings.SectionStackLeniency,
                            AllowUnsafeTickRateOverrideValues = settings.AllowUnsafeTickRateOverrideValues,
                            SectionTickRate = settings.SectionTickRate,
                            ForceHidden = settings.ForceHidden,
                            ForceNoApproachCircle = settings.ForceNoApproachCircle,
                            ForceHardRock = settings.ForceHardRock,
                            ForceFlashlight = settings.ForceFlashlight,
                            FlashlightRadius = settings.FlashlightRadius,
                        }
                    };
                }).ToList(),
            };

        private static void applyOrRemoveEntry(BeatmapHitObjectGimmicks gimmicks, OsuHitObject hitObject, bool enabled)
        {
            var existing = gimmicks.Entries.FirstOrDefault(e =>
                e.StartTime == hitObject.StartTime && e.ComboIndexWithOffsets == hitObject.ComboIndexWithOffsets);

            if (!enabled)
            {
                if (existing != null)
                {
                    existing.Settings.ForceNoApproachCircle = false;
                    cleanupEntryIfEmpty(gimmicks, existing);
                }

                return;
            }

            if (existing == null)
            {
                existing = new HitObjectGimmickEntry
                {
                    StartTime = hitObject.StartTime,
                    ComboIndexWithOffsets = hitObject.ComboIndexWithOffsets,
                    Settings = new HitObjectGimmickSettings(),
                };

                gimmicks.Entries.Add(existing);
            }

            existing.Settings.ForceNoApproachCircle = true;
            cleanupEntryIfEmpty(gimmicks, existing);
        }

        private bool getSelectionBoolState(System.Func<HitObjectGimmickSettings, bool> getter)
        {
            var selected = editorBeatmap.SelectedHitObjects.OfType<OsuHitObject>().ToList();

            if (selected.Count == 0)
                return false;

            var lookup = createLookup(gimmicks);
            return selected.All(h =>
            {
                if (!tryGetSettings(h, lookup, out var settings))
                    return false;

                return getter(settings);
            });
        }

        private HitObjectGimmickEntry getOrCreateEntry(BeatmapHitObjectGimmicks gimmicks, OsuHitObject hitObject)
        {
            // Per-object identity only: an object keeps its own entry even when another object
            // shares the same (StartTime, ComboIndexWithOffsets) legacy key.
            long objectId = MosuGimmickApplier.GetObjectId(hitObject);

            var byId = gimmicks.Entries.LastOrDefault(e => e.ObjectId == objectId);

            if (byId != null)
            {
                byId.HitObjectIndex = playableBeatmap.HitObjects.IndexOf(hitObject);
                return byId;
            }

            var existing = new HitObjectGimmickEntry
            {
                ObjectId = objectId,
                StartTime = hitObject.StartTime,
                ComboIndexWithOffsets = hitObject.ComboIndexWithOffsets,
                HitObjectIndex = playableBeatmap.HitObjects.IndexOf(hitObject),
                Settings = new HitObjectGimmickSettings(),
            };

            gimmicks.Entries.Add(existing);
            return existing;
        }

        private static void cleanupEntryIfEmpty(BeatmapHitObjectGimmicks gimmicks, HitObjectGimmickEntry entry)
        {
            var s = entry.Settings;

            bool hasAny = s.EnableHPGimmick
                          || s.IsFakeNote
                          || s.FakePunishMode != FakePunishMode.None
                          || s.FakePlayHitsound
                          || s.FakeAutoHitOnApproachClose
                          || s.FakeAutoHitPlayHitsound
                          || !s.FakeRevealEnabled
                          || Math.Abs(s.FakeRevealRed - 1f) > 0.0001f
                          || Math.Abs(s.FakeRevealGreen - 0.3019608f) > 0.0001f
                          || Math.Abs(s.FakeRevealBlue - 0.3019608f) > 0.0001f
                          || Math.Abs(s.FakeRevealStrength - HitObjectGimmickSettings.DEFAULT_FAKE_REVEAL_STRENGTH) > 0.0001f
                          || Math.Abs(s.FakeRevealLeadInStartMs - HitObjectGimmickSettings.DEFAULT_FAKE_REVEAL_LEAD_IN_START_MS) > 0.0001f
                          || Math.Abs(s.FakeRevealLeadInLengthMs - HitObjectGimmickSettings.DEFAULT_FAKE_REVEAL_LEAD_IN_LENGTH_MS) > 0.0001f
                          || Math.Abs(s.FakeRevealFadeOutStartMs - HitObjectGimmickSettings.DEFAULT_FAKE_REVEAL_FADE_OUT_START_MS) > 0.0001f
                          || Math.Abs(s.FakeRevealFadeOutLengthMs - HitObjectGimmickSettings.DEFAULT_FAKE_REVEAL_FADE_OUT_LENGTH_MS) > 0.0001f
                          || s.EnableNoMiss
                          || s.EnableCountLimits
                          || s.EnableGreatOffsetPenalty
                          || s.EnableDifficultyOverrides
                          || s.AllowUnsafeDifficultyOverrideValues
                          || s.ForceHidden
                          || s.ForceNoApproachCircle
                          || s.ForceHardRock
                          || s.ForceFlashlight
                          || s.Max300s >= 0
                          || s.Max100s >= 0
                          || s.Max50s >= 0
                          || s.MaxMisses >= 0
                          || !float.IsNaN(s.HP300)
                          || !float.IsNaN(s.HP100)
                          || !float.IsNaN(s.HP50)
                          || !float.IsNaN(s.HPMiss)
                          || s.GreatOffsetThresholdMs >= 0
                          || !float.IsNaN(s.GreatOffsetPenaltyHP)
                          || !float.IsNaN(s.SectionCircleSize)
                          || !float.IsNaN(s.SectionApproachRate)
                          || !float.IsNaN(s.SectionOverallDifficulty)
                          || s.AllowUnsafeStackLeniencyOverrideValues
                          || !float.IsNaN(s.SectionStackLeniency)
                          || s.AllowUnsafeTickRateOverrideValues
                          || !double.IsNaN(s.SectionTickRate)
                          || !float.IsNaN(s.FlashlightRadius);

            if (!hasAny)
                gimmicks.Entries.Remove(entry);
        }

        private static Dictionary<long, HitObjectGimmickSettings> createLookup(BeatmapHitObjectGimmicks gimmicks)
        {
            var dict = new Dictionary<long, HitObjectGimmickSettings>();

            for (int i = gimmicks.Entries.Count - 1; i >= 0; i--)
            {
                var e = gimmicks.Entries[i];

                if (e.ObjectId.HasValue)
                    dict[e.ObjectId.Value] = e.Settings;
            }

            return dict;
        }

        private static bool tryGetSettings(OsuHitObject hitObject,
                                           Dictionary<long, HitObjectGimmickSettings> objectIdLookup,
                                           out HitObjectGimmickSettings settings)
            => objectIdLookup.TryGetValue(MosuGimmickApplier.GetObjectId(hitObject), out settings!);

        private static bool isNoApproachForced(OsuHitObject hitObject,
                                               Dictionary<long, HitObjectGimmickSettings> objectIdLookup)
            => tryGetSettings(hitObject, objectIdLookup, out HitObjectGimmickSettings? settings) && settings.ForceNoApproachCircle;
    }
}
