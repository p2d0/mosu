// MOsu's own OsuHitObject override, hosting the delta fork's gimmick state.
//
// In the delta fork, the section/hitobject gimmick additions live on the core
// osu.Game types (HitObject.GimmickObjectId, Beatmap.SectionGimmicks, ...).
// The NuGet package does not expose any of that, so MOsu hosts the gimmick
// state on its own hitobject base instead.
//
// Ported from delta fork (osu.Game.Rulesets.Osu/Objects/OsuHitObject.cs additions).
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Game.Rulesets.MOsu.Delta.Gimmicks;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Delta.Objects
{
    public abstract class DeltaHitObject : OsuHitObject
    {
        /// <summary>
        /// Whether this hitobject should have Hidden (HD) effect applied.
        /// Set by section gimmicks during PostProcess.
        /// </summary>
        public bool ForceHidden { get; set; }

        /// <summary>
        /// Whether this hitobject should hide its approach circle only.
        /// Set by section gimmicks during PostProcess.
        /// </summary>
        public bool ForceNoApproachCircle { get; set; }

        /// <summary>
        /// Whether this hitobject should have Traceable (TC) visuals applied.
        /// Set by section or object gimmicks during PostProcess.
        /// </summary>
        public bool ForceTraceable { get; set; }

        /// <summary>
        /// Baseline non-HR position used to make section force-HR reversible.
        /// </summary>
        public Vector2? ForceHardRockBaselinePosition { get; set; }

        /// <summary>
        /// Whether force-HR transform has been applied to this object in the current processing cycle.
        /// </summary>
        public bool ForceHardRockIsApplied { get; set; }

        /// <summary>
        /// Stable identifier used to bind persisted hitobject gimmick entries to this object.
        /// The delta fork stores this on the core <see cref="HitObject"/>; MOsu hosts it here.
        /// </summary>
        public long? GimmickObjectId { get; set; }

        /// <summary>
        /// The section gimmick section covering this object's start time, if any.
        /// Resolved during beatmap processing; consumed at runtime by the health processor.
        /// </summary>
        public SectionGimmickSection? GimmickSection { get; set; }

        /// <summary>
        /// Per-object gimmick settings (fake notes, per-object overrides), if any.
        /// </summary>
        public HitObjectGimmickSettings? HitObjectGimmicks { get; set; }

        /// <summary>
        /// Merges the resolved section gimmicks with the per-object gimmicks into the
        /// effective settings used by the health processor (HP/count/offset-penalty subset).
        /// </summary>
        public SectionGimmickSettings GetEffectiveGimmicks()
            => MergeGimmickSettings(GimmickSection?.Settings, HitObjectGimmicks);

        /// <summary>
        /// Resolves the effective gimmick settings for any hitobject, returning null when
        /// the object is not a MOsu hitobject or carries no gimmicks.
        /// </summary>
        public static SectionGimmickSettings? GetEffectiveGimmicksFor(HitObject hitObject)
        {
            if (hitObject is not DeltaHitObject mosuHitObject)
                return null;

            if (mosuHitObject.GimmickSection == null && mosuHitObject.HitObjectGimmicks == null)
                return null;

            return mosuHitObject.GetEffectiveGimmicks();
        }

        private static SectionGimmickSettings MergeGimmickSettings(SectionGimmickSettings? sectionSettings, HitObjectGimmickSettings? objectSettings)
        {
            var result = new SectionGimmickSettings();

            if (sectionSettings != null)
            {
                result.EnableHPGimmick = sectionSettings.EnableHPGimmick;
                result.EnableNoMiss = sectionSettings.EnableNoMiss;
                result.EnableAccuracyRequirement = sectionSettings.EnableAccuracyRequirement;
                result.RequiredAccuracy = sectionSettings.RequiredAccuracy;
                result.EnableCountLimits = sectionSettings.EnableCountLimits;
                result.EnableNoMissedSliderEnd = sectionSettings.EnableNoMissedSliderEnd;
                result.EnableGreatOffsetPenalty = sectionSettings.EnableGreatOffsetPenalty;

                result.Max300s = sectionSettings.Max300s;
                result.Max100s = sectionSettings.Max100s;
                result.Max50s = sectionSettings.Max50s;
                result.MaxMisses = sectionSettings.MaxMisses;

                result.HP300 = sectionSettings.HP300;
                result.HP100 = sectionSettings.HP100;
                result.HP50 = sectionSettings.HP50;
                result.HPMiss = sectionSettings.HPMiss;
                result.HPStart = sectionSettings.HPStart;
                result.HPCap = sectionSettings.HPCap;
                result.HP300AffectsSliderEndsAndTicks = sectionSettings.HP300AffectsSliderEndsAndTicks;
                result.HP100AffectsSliderEndsAndTicks = sectionSettings.HP100AffectsSliderEndsAndTicks;
                result.HP50AffectsSliderEndsAndTicks = sectionSettings.HP50AffectsSliderEndsAndTicks;
                result.HPMissAffectsSliderEndAndTickMisses = sectionSettings.HPMissAffectsSliderEndAndTickMisses;
                result.Max300sAffectsSliderEndsAndTicks = sectionSettings.Max300sAffectsSliderEndsAndTicks;
                result.Max100sAffectsSliderEndsAndTicks = sectionSettings.Max100sAffectsSliderEndsAndTicks;
                result.Max50sAffectsSliderEndsAndTicks = sectionSettings.Max50sAffectsSliderEndsAndTicks;
                result.MaxMissesAffectsSliderEndAndTickMisses = sectionSettings.MaxMissesAffectsSliderEndAndTickMisses;
                result.NoDrain = sectionSettings.NoDrain;
                result.ReverseHP = sectionSettings.ReverseHP;

                result.GreatOffsetThresholdMs = sectionSettings.GreatOffsetThresholdMs;
                result.GreatOffsetPenaltyHP = sectionSettings.GreatOffsetPenaltyHP;
            }

            if (objectSettings != null)
            {
                result.EnableHPGimmick = result.EnableHPGimmick || objectSettings.EnableHPGimmick;
                result.EnableNoMiss = result.EnableNoMiss || objectSettings.EnableNoMiss;
                result.EnableCountLimits = result.EnableCountLimits || objectSettings.EnableCountLimits;
                result.EnableGreatOffsetPenalty = result.EnableGreatOffsetPenalty || objectSettings.EnableGreatOffsetPenalty;

                if (objectSettings.Max300s >= 0) result.Max300s = objectSettings.Max300s;
                if (objectSettings.Max100s >= 0) result.Max100s = objectSettings.Max100s;
                if (objectSettings.Max50s >= 0) result.Max50s = objectSettings.Max50s;
                if (objectSettings.MaxMisses >= 0) result.MaxMisses = objectSettings.MaxMisses;

                if (!float.IsNaN(objectSettings.HP300)) result.HP300 = objectSettings.HP300;
                if (!float.IsNaN(objectSettings.HP100)) result.HP100 = objectSettings.HP100;
                if (!float.IsNaN(objectSettings.HP50)) result.HP50 = objectSettings.HP50;
                if (!float.IsNaN(objectSettings.HPMiss)) result.HPMiss = objectSettings.HPMiss;

                if (objectSettings.GreatOffsetThresholdMs >= 0) result.GreatOffsetThresholdMs = objectSettings.GreatOffsetThresholdMs;
                if (!float.IsNaN(objectSettings.GreatOffsetPenaltyHP)) result.GreatOffsetPenaltyHP = objectSettings.GreatOffsetPenaltyHP;
            }

            return result;
        }
    }
}
