// Applies parsed gimmick data to the playable beatmap's hitobjects.
//
// Mirrors the delta fork's OsuBeatmapProcessor.PostProcess gimmick stages
// (fake note conversion, per-section/per-object difficulty overrides, forced
// mod flags), adapted to run on stock NuGet hitobjects after conversion.
//
// Ported from delta fork (osu.Game.Rulesets.Osu/Beatmaps/OsuBeatmapProcessor.cs).
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Logging;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.MOsu.Delta.Beatmaps;
using osu.Game.Rulesets.MOsu.Delta.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Delta.Gimmicks
{
    public static class DeltaGimmickApplier
    {
        public const float PLAYFIELD_HEIGHT = 384;

        private sealed class ObjectIdRef
        {
            public long Value;
        }

        /// <summary>
        /// Per-object-instance identity, so two objects sharing a legacy key stay independent.
        /// Persisted as the entry's ObjectId across save/load. Only used for stock hitobject
        /// types; delta objects carry the id on <see cref="DeltaHitObject.GimmickObjectId"/>.
        /// </summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<osu.Game.Rulesets.Osu.Objects.OsuHitObject, ObjectIdRef> object_ids =
            new System.Runtime.CompilerServices.ConditionalWeakTable<osu.Game.Rulesets.Osu.Objects.OsuHitObject, ObjectIdRef>();

        // Start far above any persisted (delta) object id so counter-assigned fresh ids can
        // never collide with ids loaded from a .osu file's gimmick section.
        private static long nextObjectId = 1_000_000_000;

        private static long getId(osu.Game.Rulesets.Osu.Objects.OsuHitObject o)
        {
            // Delta objects host their id on the object; the side table only serves stock
            // circle/spinner types (which cannot carry delta state).
            if (o is DeltaHitObject delta && delta.GimmickObjectId.HasValue)
                return delta.GimmickObjectId.Value;

            if (object_ids.TryGetValue(o, out var r))
                return r.Value;

            r = new ObjectIdRef { Value = nextObjectId++ };
            object_ids.Add(o, r);
            return r.Value;
        }

        private static void setId(osu.Game.Rulesets.Osu.Objects.OsuHitObject o, long value)
        {
            if (o is DeltaHitObject delta)
            {
                delta.GimmickObjectId = value;
                return;
            }

            if (object_ids.TryGetValue(o, out var r))
                r.Value = value;
            else
                object_ids.Add(o, new ObjectIdRef { Value = value });
        }

        /// <summary>
        /// Applies per-section/per-object difficulty overrides and forced mod flags.
        /// Idempotent per beatmap instance via <paramref name="data"/>.<see cref="DeltaGimmickData.Applied"/>.
        /// Only mutates object properties in place - never the hitobject list (the drawable
        /// ruleset enumerates it while creating drawables).
        /// </summary>
        public static void Apply(IBeatmap beatmap, DeltaGimmickData data, bool mutateList = true, IReadOnlyList<Mod>? mods = null)
        {
            if (data.Applied)
            {
                Logger.Log($"[MOsu] apply skipped: already applied (entries={data.HitObjectGimmicks.Entries.Count})");
                return;
            }

            data.Applied = true;
            Logger.Log($"[MOsu] applying to {beatmap.HitObjects.Count} objects: {data.Sections.Sections.Count} sections, {data.HitObjectGimmicks.Entries.Count} entries");

            synchroniseEntriesWithHitObjects(beatmap, data);
            bindObjectSettings(beatmap, data);
            applySectionDifficultyOverrides(beatmap, data, mods);
            applySectionForcedMods(beatmap, data);

            // Delta-style: replace fake-note sources in the playable's HitObjects list in place.
            // The max-combo autoplay simulation then judges the Fake* objects (IgnoreHit, no
            // combo) and the drawable ruleset creates fake drawables for them directly.
            // Skipped in the editor, where the playable must keep the original object instances
            // (EditorBeatmap keys its internal dictionaries by them).
            if (mutateList)
                replaceFakeObjects(beatmap, data, mods);

            foreach (var o in beatmap.HitObjects.OfType<osu.Game.Rulesets.Osu.Objects.OsuHitObject>().Where(o => o is HitCircle || o is Slider))
            {
                var s = getObjectSettings(o, createObjectSettingsLookup(data.HitObjectGimmicks));
                if (s?.EnableDifficultyOverrides == true && !float.IsNaN(s.SectionCircleSize))
                    Logger.Log($"[MOsu] apply: object@{o.StartTime} CS={s.SectionCircleSize} Scale={o.Scale}");
            }
        }

        /// <summary>
        /// Replaces objects with fake-note settings in the playable's HitObjects list with
        /// <see cref="FakeHitCircle"/> / <see cref="FakeSlider"/> instances, in place (same
        /// index, same count) so any in-flight enumeration stays valid.
        /// </summary>
        private static void replaceFakeObjects(IBeatmap beatmap, DeltaGimmickData data, IReadOnlyList<Mod>? mods)
        {
            if (beatmap is not Beatmaps.DeltaBeatmap mosuBeatmap)
                return;

            var list = (System.Collections.Generic.List<osu.Game.Rulesets.Osu.Objects.OsuHitObject>)mosuBeatmap.HitObjects;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is not osu.Game.Rulesets.Osu.Objects.OsuHitObject osuObject)
                    continue;

                var settings = GetObjectSettings(beatmap, data, osuObject);

                // undo: a fake object whose flag was cleared becomes a normal object again
                if (osuObject is FakeHitCircle or FakeSlider && settings?.IsFakeNote != true)
                {
                    var normal = createNormalObject(osuObject);
                    if (normal == null)
                        continue;

                    normal.ApplyDefaults(beatmap.ControlPointInfo, ResolveDifficultyForObject(beatmap, data, osuObject));
                    applyHiddenFadeInAdjustmentIfNeeded(normal, mods);
                    list[i] = normal;
                    Logger.Log($"[MOsu] restored object@{osuObject.StartTime} to {normal.GetType().Name}");
                    continue;
                }

                if (osuObject is FakeHitCircle or FakeSlider || settings?.IsFakeNote != true)
                    continue;

                var fake = CreateFakeObject(beatmap, data, osuObject);
                if (fake == null)
                    continue;

                fake.ApplyDefaults(beatmap.ControlPointInfo, ResolveDifficultyForObject(beatmap, data, osuObject));
                applyHiddenFadeInAdjustmentIfNeeded(fake, mods);
                ApplyForcedModsToObject(beatmap, data, fake);

                list[i] = fake;
                Logger.Log($"[MOsu] replaced object@{osuObject.StartTime} with {fake.GetType().Name}");
            }
        }

        private static osu.Game.Rulesets.Osu.Objects.OsuHitObject? createNormalObject(osu.Game.Rulesets.Osu.Objects.OsuHitObject fake)
        {
            switch (fake)
            {
                case FakeHitCircle fakeCircle:
                    return new HitCircle
                    {
                        StartTime = fakeCircle.StartTime,
                        Position = fakeCircle.Position,
                        NewCombo = fakeCircle.NewCombo,
                        ComboOffset = fakeCircle.ComboOffset,
                        Samples = fakeCircle.Samples.ToList(),
                    };

                case FakeSlider fakeSlider:
                    return new Slider
                    {
                        StartTime = fakeSlider.StartTime,
                        Position = fakeSlider.Position,
                        NewCombo = fakeSlider.NewCombo,
                        ComboOffset = fakeSlider.ComboOffset,
                        Samples = fakeSlider.Samples.ToList(),
                        RepeatCount = fakeSlider.RepeatCount,
                        Path = fakeSlider.Path,
                        SliderVelocityMultiplier = fakeSlider.SliderVelocityMultiplier,
                        GenerateTicks = fakeSlider.GenerateTicks,
                        TickDistanceMultiplier = fakeSlider.TickDistanceMultiplier,
                        ClassicSliderBehaviour = fakeSlider.ClassicSliderBehaviour,
                    };

                default:
                    return null;
            }
        }

        /// <summary>
        /// Resolves the gimmick settings bound to an object, if any.
        /// </summary>
        /// <summary>
        /// Stable per-object-instance id used to bind gimmick entries to stock hitobjects.
        /// </summary>
        public static long GetObjectId(osu.Game.Rulesets.Osu.Objects.OsuHitObject hitObject)
            => getId(hitObject);

        public static HitObjectGimmickSettings? GetObjectSettings(IBeatmap beatmap, DeltaGimmickData data, HitObject hitObject)
        {
            if (hitObject is not osu.Game.Rulesets.Osu.Objects.OsuHitObject osuHitObject)
                return null;

            var entries = data.HitObjectGimmicks.Entries;

            if (entries.Count == 0)
                return null;

            // Per-object identity wins: an id'd object resolves only to its own entry, so a circle
            // moved onto a fake's slot stays independent. Legacy key is a last resort for objects
            // not yet assigned an id (e.g. before the first apply).
            long id = getId(osuHitObject);

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].ObjectId == id)
                    return entries[i].Settings;
            }

            // no entry for this object's id
            return null;
        }

        /// <summary>
        /// Assigns per-object ids, matches entries to objects (id first, legacy fallback) and
        /// binds each object to its resolved settings.
        /// </summary>
        private static void bindObjectSettings(IBeatmap beatmap, DeltaGimmickData data)
        {
            var objects = beatmap.HitObjects.OfType<osu.Game.Rulesets.Osu.Objects.OsuHitObject>().ToList();
            var entries = data.HitObjectGimmicks.Entries;

            // assign ids to all objects so reads are id-based after the first apply
            foreach (var o in objects)
                getId(o);

            // match entries to objects: by persisted id, then persisted index, then legacy key; newest wins.
            var claimed = new HashSet<osu.Game.Rulesets.Osu.Objects.OsuHitObject>();

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];

                var byId = entry.ObjectId.HasValue
                    ? objects.FirstOrDefault(o => !claimed.Contains(o) && getId(o) == entry.ObjectId.Value)
                    : null;

                var byIndex = byId == null && entry.HitObjectIndex.HasValue && entry.HitObjectIndex.Value >= 0 && entry.HitObjectIndex.Value < objects.Count
                    ? objects[entry.HitObjectIndex.Value]
                    : null;

                if (byIndex != null && (claimed.Contains(byIndex)
                    || byIndex.StartTime != entry.StartTime || byIndex.ComboIndexWithOffsets != entry.ComboIndexWithOffsets))
                    byIndex = null;

                var matched = byId ?? byIndex
                              ?? objects.FirstOrDefault(o => !claimed.Contains(o)
                                  && o.StartTime == entry.StartTime && o.ComboIndexWithOffsets == entry.ComboIndexWithOffsets);

                if (matched == null)
                    continue;

                claimed.Add(matched);

                // align the object's id with the entry so reads and save/load stay consistent
                if (entry.ObjectId.HasValue)
                {
                    bool taken = objects.Any(other => !ReferenceEquals(other, matched) && getId(other) == entry.ObjectId.Value);

                    if (taken)
                        entry.ObjectId = getId(matched);
                    else
                        setId(matched, entry.ObjectId.Value);
                }
                else
                    entry.ObjectId = getId(matched);

                // record the object's current list index so reloads can disambiguate shared keys
                entry.HitObjectIndex = objects.IndexOf(matched);
            }

            // persist matched ids back onto entries so saves write the object's real id, and
            // bind the resolved settings onto delta objects (consumed by the health processor).
            foreach (var o in objects)
            {
                long id = getId(o);

                for (int i = entries.Count - 1; i >= 0; i--)
                {
                    if (entries[i].ObjectId == id)
                    {
                        if (o is DeltaHitObject delta)
                            delta.HitObjectGimmicks = entries[i].Settings;
                        break;
                    }
                }
            }

        }

        /// <summary>
        /// Creates a fake-note hitobject for the given source object, copying the source's
        /// already-overridden state. Returns null when the object is not a fake note.
        /// </summary>
        public static osu.Game.Rulesets.Osu.Objects.OsuHitObject? CreateFakeObject(IBeatmap beatmap, DeltaGimmickData data, osu.Game.Rulesets.Osu.Objects.OsuHitObject source)
        {
            var objectSettings = GetObjectSettings(beatmap, data, source);

            if (objectSettings?.IsFakeNote != true)
                return null;

            var fakePunishMode = normaliseFakePunishMode(objectSettings.FakePunishMode);

            switch (source)
            {
                case HitCircle hitCircle:
                {
                    var fakeCircle = new FakeHitCircle
                    {
                        FakePunishMode = fakePunishMode,
                        FakePlayHitsound = objectSettings.FakePlayHitsound,
                        FakeAutoHitOnApproachClose = objectSettings.FakeAutoHitOnApproachClose,
                        FakeAutoHitPlayHitsound = objectSettings.FakeAutoHitPlayHitsound,
                        FakeRevealEnabled = objectSettings.FakeRevealEnabled,
                        FakeRevealRed = objectSettings.FakeRevealRed,
                        FakeRevealGreen = objectSettings.FakeRevealGreen,
                        FakeRevealBlue = objectSettings.FakeRevealBlue,
                        FakeRevealStrength = objectSettings.FakeRevealStrength,
                        FakeRevealLeadInStartMs = objectSettings.FakeRevealLeadInStartMs,
                        FakeRevealLeadInLengthMs = objectSettings.FakeRevealLeadInLengthMs,
                        FakeRevealFadeOutStartMs = objectSettings.FakeRevealFadeOutStartMs,
                        FakeRevealFadeOutLengthMs = objectSettings.FakeRevealFadeOutLengthMs,
                    };

                    copyCommonOsuValues(hitCircle, fakeCircle);

                    // Selecting a fake note in the editor picks the drawable's HitObject (the
                    // clone); give it the source's id so the toolbox resolves its settings.
                    setId(fakeCircle, getId(source));
                    return fakeCircle;
                }

                case Slider slider:
                {
                    var fakeSlider = new FakeSlider
                    {
                        RepeatCount = slider.RepeatCount,
                        Path = slider.Path,
                        SliderVelocityMultiplier = slider.SliderVelocityMultiplier,
                        GenerateTicks = slider.GenerateTicks,
                        TickDistanceMultiplier = slider.TickDistanceMultiplier,
                        ClassicSliderBehaviour = slider.ClassicSliderBehaviour,

                        FakePunishMode = fakePunishMode,
                        FakePlayHitsound = objectSettings.FakePlayHitsound,
                        FakeAutoHitOnApproachClose = objectSettings.FakeAutoHitOnApproachClose,
                        FakeAutoHitPlayHitsound = objectSettings.FakeAutoHitPlayHitsound,
                        FakeRevealEnabled = objectSettings.FakeRevealEnabled,
                        FakeRevealRed = objectSettings.FakeRevealRed,
                        FakeRevealGreen = objectSettings.FakeRevealGreen,
                        FakeRevealBlue = objectSettings.FakeRevealBlue,
                        FakeRevealStrength = objectSettings.FakeRevealStrength,
                        FakeRevealLeadInStartMs = objectSettings.FakeRevealLeadInStartMs,
                        FakeRevealLeadInLengthMs = objectSettings.FakeRevealLeadInLengthMs,
                        FakeRevealFadeOutStartMs = objectSettings.FakeRevealFadeOutStartMs,
                        FakeRevealFadeOutLengthMs = objectSettings.FakeRevealFadeOutLengthMs,
                    };

                    copyCommonOsuValues(slider, fakeSlider);
                    fakeSlider.NodeSamples = slider.NodeSamples.Select(samples => (IList<HitSampleInfo>)samples.Select(s => s.With()).ToList()).ToList();

                    setId(fakeSlider, getId(source));
                    return fakeSlider;
                }

                default:
                    return null;
            }
        }

        /// <summary>
        /// Re-binds gimmick entries whose legacy key no longer matches any hitobject.
        /// When an entry's (StartTime, ComboIndexWithOffsets) is missing but an object with
        /// the same StartTime exists, rebind the entry to that object. Keeps matching correct
        /// when combo indices drift across an encode/decode round trip.
        /// </summary>
        private static void synchroniseEntriesWithHitObjects(IBeatmap beatmap, DeltaGimmickData data)
        {
            if (data.HitObjectGimmicks.Entries.Count == 0)
                return;

            var objects = beatmap.HitObjects.OfType<osu.Game.Rulesets.Osu.Objects.OsuHitObject>().ToList();

            var objectKeys = objects
                             .Select(o => (o.StartTime, o.ComboIndexWithOffsets))
                             .ToHashSet();

            foreach (var entry in data.HitObjectGimmicks.Entries)
            {
                if (objectKeys.Contains((entry.StartTime, entry.ComboIndexWithOffsets)))
                    continue;

                // Combo drifted on round trip: rebind to the object with the same start time.
                var candidate = objects.FirstOrDefault(o => o.StartTime == entry.StartTime);
                if (candidate != null)
                {
                    entry.StartTime = candidate.StartTime;
                    entry.ComboIndexWithOffsets = candidate.ComboIndexWithOffsets;
                }
            }
        }

        private static Dictionary<(double StartTime, int ComboIndexWithOffsets), HitObjectGimmickSettings> createObjectSettingsLookup(BeatmapHitObjectGimmicks gimmicks)
            => HitObjectGimmickBindingUtils.CreateLookupByLegacyKey(gimmicks);

        private static HitObjectGimmickSettings? getObjectSettings(HitObject hitObject, Dictionary<(double StartTime, int ComboIndexWithOffsets), HitObjectGimmickSettings> lookup)
            => hitObject is osu.Game.Rulesets.Osu.Objects.OsuHitObject osuHitObject
               && lookup.TryGetValue((osuHitObject.StartTime, osuHitObject.ComboIndexWithOffsets), out var settings)
                ? settings
                : null;

        private static SectionGimmickSection? resolveSection(BeatmapSectionGimmicks gimmicks, double time)
            => gimmicks.FindSectionAt(time);

        private static FakePunishMode normaliseFakePunishMode(FakePunishMode mode)
            => mode == FakePunishMode.None ? FakePunishMode.None : FakePunishMode.Miss;

        private static void copyCommonOsuValues(osu.Game.Rulesets.Osu.Objects.OsuHitObject source, osu.Game.Rulesets.Osu.Objects.OsuHitObject target)
        {
            target.StartTime = source.StartTime;
            target.Position = source.Position;
            target.NewCombo = source.NewCombo;
            target.ComboOffset = source.ComboOffset;
            target.Samples = source.Samples.ToList();

            target.ComboIndex = source.ComboIndex;
            target.ComboIndexWithOffsets = source.ComboIndexWithOffsets;
            target.IndexInCurrentCombo = source.IndexInCurrentCombo;
            target.LastInCombo = source.LastInCombo;
        }

        private static void applySectionForcedMods(IBeatmap beatmap, DeltaGimmickData data)
        {
            var objectSettingsLookup = createObjectSettingsLookup(data.HitObjectGimmicks);

            foreach (var hitObject in beatmap.HitObjects.OfType<osu.Game.Rulesets.Osu.Objects.OsuHitObject>())
                ApplyForcedModsToObject(beatmap, data, hitObject);
        }

        private static void applyHiddenEffect(osu.Game.Rulesets.Osu.Objects.OsuHitObject hitObject)
        {
            // match osu! HD's fade-in adjustment so hidden sections fade out before being hit.
            hitObject.TimeFadeIn = hitObject.TimePreempt * 0.4;

            foreach (var nested in hitObject.NestedHitObjects.OfType<osu.Game.Rulesets.Osu.Objects.OsuHitObject>())
                applyHiddenEffect(nested);
        }

        private static void applyHardRockTransforms(osu.Game.Rulesets.Osu.Objects.OsuHitObject hitObject)
        {
            hitObject.Position = new Vector2(hitObject.Position.X, PLAYFIELD_HEIGHT - hitObject.Position.Y);

            if (hitObject is Slider slider)
                slider.Path = flipPathRelativeToStart(slider.Path);
        }

        private static SliderPath flipPathRelativeToStart(SliderPath source)
        {
            var flipped = new SliderPath();

            if (source.ControlPoints.Count == 0)
                return flipped;

            // Control points are relative to slider start position.
            // HR vertical flip around playfield centre in absolute space becomes sign inversion of relative Y.
            flipped.ControlPoints.AddRange(source.ControlPoints.Select(c => new PathControlPoint(new Vector2(c.Position.X, -c.Position.Y), c.Type)));
            flipped.ExpectedDistance.Value = source.ExpectedDistance.Value;
            return flipped;
        }

        private static void applySectionDifficultyOverrides(IBeatmap beatmap, DeltaGimmickData data, IReadOnlyList<Mod>? mods)
        {
            var orderedSections = data.Sections.Sections.OrderBy(s => s.StartTime).ToList();
            var baseDifficulty = beatmap.Difficulty;

            var sectionGradualBaselines = new Dictionary<int, BeatmapDifficulty>();

            var objectSettingsLookup = createObjectSettingsLookup(data.HitObjectGimmicks);

            foreach (var hitObject in beatmap.HitObjects.OfType<osu.Game.Rulesets.Osu.Objects.OsuHitObject>())
            {
                var difficulty = ResolveDifficultyForObject(beatmap, data, hitObject, sectionGradualBaselines);
                hitObject.ApplyDefaults(beatmap.ControlPointInfo, difficulty);
                applyHiddenFadeInAdjustmentIfNeeded(hitObject, mods);
            }
        }

        /// <summary>
        /// Re-applies the Hidden mod's fade-in adjustment after a runtime <see cref="HitObject.ApplyDefaults"/>
        /// pass, which resets <see cref="OsuHitObject.TimeFadeIn"/> and would otherwise leave notes
        /// fully visible until the hit (no early fade-out). Mirrors <see cref="OsuModHidden.ApplyToBeatmap"/>:
        /// non-slider OsuHitObjects (and their nested objects) fade in at a fraction of the (possibly
        /// section-AR-overridden) preempt. Only the real mod (or section ForceHidden, applied later in
        /// the pass) should influence this, so the adjustment is keyed to the selected mods.
        /// </summary>
        private static void applyHiddenFadeInAdjustmentIfNeeded(osu.Game.Rulesets.Osu.Objects.OsuHitObject osuObject, IReadOnlyList<Mod>? mods)
        {
            if (mods?.OfType<OsuModHidden>().Any() != true)
                return;

            ApplyHiddenFadeInAdjustment(osuObject);
        }

        /// <summary>
        /// Mirrors <see cref="OsuModHidden.ApplyToBeatmap"/>: re-applies the hidden fade-in ratio
        /// (non-slider OsuHitObjects and their nested objects fade in at a fraction of the preempt)
        /// after a runtime <see cref="HitObject.ApplyDefaults"/> pass, which resets
        /// <see cref="OsuHitObject.TimeFadeIn"/> and would otherwise leave notes fully visible
        /// until the hit (no early fade-out).
        /// </summary>
        public static void ApplyHiddenFadeInAdjustment(osu.Game.Rulesets.Osu.Objects.OsuHitObject osuObject)
        {
            if (osuObject is not Slider)
                osuObject.TimeFadeIn = osuObject.TimePreempt * OsuModHidden.FADE_IN_DURATION_MULTIPLIER;

            foreach (var nested in osuObject.NestedHitObjects.OfType<osu.Game.Rulesets.Osu.Objects.OsuHitObject>())
                ApplyHiddenFadeInAdjustment(nested);
        }

        /// <summary>
        /// Resolves the effective difficulty (with section/object overrides) for a single object.
        /// Shared by the apply pass and fake-note creation so fakes match their source exactly.
        /// </summary>
        public static BeatmapDifficulty ResolveDifficultyForObject(IBeatmap beatmap, DeltaGimmickData data,
                                                                   osu.Game.Rulesets.Osu.Objects.OsuHitObject hitObject,
                                                                   Dictionary<int, BeatmapDifficulty>? sectionGradualBaselines = null)
        {
            var orderedSections = data.Sections.Sections.OrderBy(s => s.StartTime).ToList();
            var baseDifficulty = beatmap.Difficulty;

            SectionGimmickSection? section = resolveSection(data.Sections, hitObject.StartTime);
            var objectSettings = getObjectSettings(hitObject, createObjectSettingsLookup(data.HitObjectGimmicks));
            var difficulty = baseDifficulty.Clone();

            if (section?.Settings.EnableDifficultyOverrides == true)
            {
                if (sectionGradualBaselines == null)
                    sectionGradualBaselines = new Dictionary<int, BeatmapDifficulty>();

                if (!sectionGradualBaselines.TryGetValue(section.Id, out var sectionBaseline))
                {
                    sectionBaseline = section.Settings.DifficultyOverrideStartWithBeatmapValues
                        ? baseDifficulty.Clone()
                        : computeSectionInheritedBaseline(orderedSections, section, baseDifficulty);

                    sectionGradualBaselines[section.Id] = sectionBaseline;
                }

                applyDifficultyOverridesForTime(section, hitObject.StartTime, difficulty, sectionBaseline);
            }
            else
            {
                var keepSection = orderedSections
                    .Where(s => s.Settings.EnableDifficultyOverrides)
                    .Where(s => s.EndTime >= 0 && s.EndTime < hitObject.StartTime)
                    .LastOrDefault();

                if (keepSection?.Settings.KeepDifficultyOverridesAfterSection == true)
                {
                    applyDifficultyOverridesForTime(keepSection, keepSection.EndTime, difficulty, baseDifficulty, allowGradual: false);
                }
                else
                {
                    difficulty.CircleSize = baseDifficulty.CircleSize;
                    difficulty.ApproachRate = baseDifficulty.ApproachRate;
                    difficulty.OverallDifficulty = baseDifficulty.OverallDifficulty;
                }
            }

            if (section?.Settings.ForceHardRock == true || objectSettings?.ForceHardRock == true)
            {
                difficulty.CircleSize = Math.Min(difficulty.CircleSize * 1.3f, 11f);
                difficulty.ApproachRate = Math.Min(difficulty.ApproachRate * 1.4f, 10f);
                difficulty.OverallDifficulty = Math.Min(difficulty.OverallDifficulty * 1.4f, 10f);
            }

            applyObjectDifficultyOverrides(objectSettings, difficulty);

            bool allowUnsafeDifficulty = (section?.Settings.EnableDifficultyOverrides == true && section.Settings.AllowUnsafeDifficultyOverrideValues)
                                         || (objectSettings?.EnableDifficultyOverrides == true && objectSettings.AllowUnsafeDifficultyOverrideValues);

            if (!allowUnsafeDifficulty)
            {
                difficulty.CircleSize = SectionGimmickValueClamper.ClampCircleSize(difficulty.CircleSize);
                difficulty.ApproachRate = SectionGimmickValueClamper.ClampApproachRate(difficulty.ApproachRate);
                difficulty.OverallDifficulty = SectionGimmickValueClamper.ClampOverallDifficulty(difficulty.OverallDifficulty);
            }

            difficulty.SliderTickRate = resolveTickRateForObject(beatmap, section, objectSettings);

            return difficulty;
        }

        /// <summary>
        /// Applies forced-mod property effects (hidden fade, hard-rock transforms) to a single object.
        /// </summary>
        public static void ApplyForcedModsToObject(IBeatmap beatmap, DeltaGimmickData data,
                                                   osu.Game.Rulesets.Osu.Objects.OsuHitObject hitObject)
        {
            SectionGimmickSection? section = resolveSection(data.Sections, hitObject.StartTime);
            var objectSettings = getObjectSettings(hitObject, createObjectSettingsLookup(data.HitObjectGimmicks));

            if (section?.Settings.ForceHardRock == true || objectSettings?.ForceHardRock == true)
                applyHardRockTransforms(hitObject);

            if (section?.Settings.ForceHidden == true || objectSettings?.ForceHidden == true)
                applyHiddenEffect(hitObject);
        }

        private static double resolveTickRateForObject(IBeatmap beatmap, SectionGimmickSection? section, HitObjectGimmickSettings? objectSettings)
        {
            double tickRate = beatmap.Difficulty.SliderTickRate;

            if (section?.Settings.EnableDifficultyOverrides == true && !double.IsNaN(section.Settings.SectionTickRate))
            {
                tickRate = section.Settings.AllowUnsafeTickRateOverrideValues
                    ? section.Settings.SectionTickRate
                    : SectionGimmickValueClamper.ClampTickRate(section.Settings.SectionTickRate);
            }

            if (objectSettings?.EnableDifficultyOverrides == true && !double.IsNaN(objectSettings.SectionTickRate))
            {
                tickRate = objectSettings.AllowUnsafeTickRateOverrideValues
                    ? objectSettings.SectionTickRate
                    : SectionGimmickValueClamper.ClampTickRate(objectSettings.SectionTickRate);
            }

            return tickRate;
        }

        private static BeatmapDifficulty computeSectionInheritedBaseline(List<SectionGimmickSection> orderedSections, SectionGimmickSection targetSection, BeatmapDifficulty baseDifficulty)
        {
            var keepSection = orderedSections
                .Where(s => s.Id != targetSection.Id)
                .Where(s => s.Settings.EnableDifficultyOverrides)
                .Where(s => s.EndTime >= 0 && s.EndTime <= targetSection.StartTime)
                .LastOrDefault();

            if (keepSection?.Settings.KeepDifficultyOverridesAfterSection == true)
            {
                var baseline = baseDifficulty.Clone();
                applyDifficultyOverridesForTime(keepSection, keepSection.EndTime, baseline, baseDifficulty, allowGradual: false);
                return baseline;
            }

            return baseDifficulty.Clone();
        }

        private static void applyObjectDifficultyOverrides(HitObjectGimmickSettings? settings, BeatmapDifficulty difficulty)
        {
            if (settings?.EnableDifficultyOverrides != true)
                return;

            if (!float.IsNaN(settings.SectionCircleSize))
                difficulty.CircleSize = settings.SectionCircleSize;

            if (!float.IsNaN(settings.SectionApproachRate))
                difficulty.ApproachRate = settings.SectionApproachRate;

            if (!float.IsNaN(settings.SectionOverallDifficulty))
                difficulty.OverallDifficulty = settings.SectionOverallDifficulty;
        }

        private static void applyDifficultyOverridesForTime(SectionGimmickSection section, double objectTime, BeatmapDifficulty targetDifficulty, IBeatmapDifficultyInfo baseDifficulty, bool allowGradual = true)
        {
            var settings = section.Settings;

            double sectionEnd = section.EndTime >= 0 ? section.EndTime : double.MaxValue;

            if (!float.IsNaN(settings.SectionCircleSize))
            {
                if (tryResolveWindowedDifficultyValue(
                        section, objectTime, sectionEnd, settings.SectionCircleSize, baseDifficulty.CircleSize,
                        settings.EnableSectionCircleSizeWindow, settings.SectionCircleSizeStartTimeMs, settings.SectionCircleSizeEndTimeMs,
                        allowGradual, settings.EnableGradualSectionCircleSizeChange, settings.EnableGradualDifficultyChange, settings.GradualDifficultyChangeEndTimeMs,
                        out float resolvedCs))
                {
                    targetDifficulty.CircleSize = resolvedCs;
                }
            }

            if (!float.IsNaN(settings.SectionApproachRate))
            {
                if (tryResolveWindowedDifficultyValue(
                        section, objectTime, sectionEnd, settings.SectionApproachRate, baseDifficulty.ApproachRate,
                        settings.EnableSectionApproachRateWindow, settings.SectionApproachRateStartTimeMs, settings.SectionApproachRateEndTimeMs,
                        allowGradual, settings.EnableGradualSectionApproachRateChange, settings.EnableGradualDifficultyChange, settings.GradualDifficultyChangeEndTimeMs,
                        out float resolvedAr))
                {
                    targetDifficulty.ApproachRate = resolvedAr;
                }
            }

            if (!float.IsNaN(settings.SectionOverallDifficulty))
            {
                if (tryResolveWindowedDifficultyValue(
                        section, objectTime, sectionEnd, settings.SectionOverallDifficulty, baseDifficulty.OverallDifficulty,
                        settings.EnableSectionOverallDifficultyWindow, settings.SectionOverallDifficultyStartTimeMs, settings.SectionOverallDifficultyEndTimeMs,
                        allowGradual, settings.EnableGradualSectionOverallDifficultyChange, settings.EnableGradualDifficultyChange, settings.GradualDifficultyChangeEndTimeMs,
                        out float resolvedOd))
                {
                    targetDifficulty.OverallDifficulty = resolvedOd;
                }
            }
        }

        private static bool tryResolveWindowedDifficultyValue(
            SectionGimmickSection section,
            double objectTime,
            double sectionEnd,
            float target,
            float baseline,
            bool hasWindow,
            float configuredStart,
            float configuredEnd,
            bool allowGradual,
            bool valueGradual,
            bool globalGradual,
            float globalGradualEndTime,
            out float resolved)
        {
            resolved = target;

            double windowStart = hasWindow && configuredStart >= 0 ? configuredStart : section.StartTime;
            double windowEnd = hasWindow && configuredEnd >= 0 ? configuredEnd : sectionEnd;

            if (windowEnd > sectionEnd)
                windowEnd = sectionEnd;

            if (windowStart > windowEnd)
                return false;

            if (objectTime < windowStart || objectTime > windowEnd)
                return false;

            bool useGradual = allowGradual && (globalGradual || valueGradual);
            if (!useGradual)
            {
                resolved = target;
                return true;
            }

            double gradualEnd;

            if (valueGradual)
            {
                gradualEnd = windowEnd;
            }
            else if (float.IsNaN(globalGradualEndTime))
            {
                gradualEnd = sectionEnd;
            }
            else
            {
                gradualEnd = globalGradualEndTime;
                if (gradualEnd > sectionEnd)
                    gradualEnd = sectionEnd;

                if (gradualEnd > windowEnd)
                    gradualEnd = windowEnd;
            }

            if (gradualEnd <= windowStart)
            {
                resolved = target;
                return true;
            }

            double progress = Math.Clamp((objectTime - windowStart) / (gradualEnd - windowStart), 0, 1);
            resolved = MathF.Round((float)(baseline + (target - baseline) * progress), 1, MidpointRounding.AwayFromZero);
            return true;
        }
    }
}
