// Serializes gimmick data to the delta fork's .osu section format
// (mirror of the delta LegacyBeatmapEncoder handleSectionGimmicks / handleHitObjectGimmicks).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using osuTK.Graphics;

namespace osu.Game.Rulesets.MOsu.Gimmicks
{
    public static class MosuGimmickSerializer
    {
        public static string Serialize(BeatmapSectionGimmicks sections, BeatmapHitObjectGimmicks hitObjectGimmicks)
        {
            var sb = new StringBuilder();

            if (sections.Sections.Count > 0)
            {
                sb.AppendLine("[BeatmapSectionGimmicks]");

                foreach (var section in sections.Sections.OrderBy(s => s.StartTime))
                {
                    var pairs = SerialiseSectionSettings(section.Settings);
                    sb.AppendLine(FormattableString.Invariant($"{section.Id},{section.StartTime},{section.EndTime},{string.Join('|', pairs)}"));
                }

                sb.AppendLine();
            }

            if (hitObjectGimmicks.Entries.Count > 0)
            {
                sb.AppendLine("[BeatmapHitObjectGimmicks]");

                foreach (var entry in hitObjectGimmicks.Entries.OrderBy(e => e.StartTime).ThenBy(e => e.ComboIndexWithOffsets))
                {
                    var pairs = SerialiseHitObjectSettings(entry.Settings);

                    string idPair = entry.ObjectId.HasValue ? $"ObjectId={entry.ObjectId.Value}|" : "";
                    string indexPair = entry.HitObjectIndex.HasValue ? $"Index={entry.HitObjectIndex.Value}|" : "";

                    sb.AppendLine(FormattableString.Invariant($"{entry.StartTime},{entry.ComboIndexWithOffsets},{idPair}{indexPair}{string.Join('|', pairs)}"));
                }
            }

            return sb.ToString();
        }

        public static IEnumerable<string> SerialiseSectionSettings(SectionGimmickSettings settings)
        {
            if (settings.EnableHPGimmick) yield return "EnableHPGimmick=True";
            if (settings.EnableNoMiss) yield return "EnableNoMiss=True";
            if (settings.EnableAccuracyRequirement) yield return "EnableAccuracyRequirement=True";
            if (!float.IsNaN(settings.RequiredAccuracy)) yield return $"RequiredAccuracy={inv(settings.RequiredAccuracy)}";
            if (settings.EnableCountLimits) yield return "EnableCountLimits=True";
            if (settings.EnableNoMissedSliderEnd) yield return "EnableNoMissedSliderEnd=True";
            if (settings.EnableGreatOffsetPenalty) yield return "EnableGreatOffsetPenalty=True";

            if (settings.Max300s >= 0) yield return $"Max300s={settings.Max300s}";
            if (settings.Max100s >= 0) yield return $"Max100s={settings.Max100s}";
            if (settings.Max50s >= 0) yield return $"Max50s={settings.Max50s}";
            if (settings.MaxMisses >= 0) yield return $"MaxMisses={settings.MaxMisses}";
            if (settings.Max300sAffectsSliderEndsAndTicks) yield return "Max300sAffectsSliderEndsAndTicks=True";
            if (settings.Max100sAffectsSliderEndsAndTicks) yield return "Max100sAffectsSliderEndsAndTicks=True";
            if (settings.Max50sAffectsSliderEndsAndTicks) yield return "Max50sAffectsSliderEndsAndTicks=True";
            if (settings.MaxMissesAffectsSliderEndAndTickMisses) yield return "MaxMissesAffectsSliderEndAndTickMisses=True";

            if (!float.IsNaN(settings.HP300)) yield return $"HP300={inv(settings.HP300)}";
            if (!float.IsNaN(settings.HP100)) yield return $"HP100={inv(settings.HP100)}";
            if (!float.IsNaN(settings.HP50)) yield return $"HP50={inv(settings.HP50)}";
            if (!float.IsNaN(settings.HPMiss)) yield return $"HPMiss={inv(settings.HPMiss)}";
            if (!float.IsNaN(settings.HPStart)) yield return $"HPStart={inv(settings.HPStart)}";
            if (!float.IsNaN(settings.HPCap)) yield return $"HPCap={inv(settings.HPCap)}";
            if (settings.HP300AffectsSliderEndsAndTicks) yield return "HP300AffectsSliderEndsAndTicks=True";
            if (settings.HP100AffectsSliderEndsAndTicks) yield return "HP100AffectsSliderEndsAndTicks=True";
            if (settings.HP50AffectsSliderEndsAndTicks) yield return "HP50AffectsSliderEndsAndTicks=True";
            if (settings.HPMissAffectsSliderEndAndTickMisses) yield return "HPMissAffectsSliderEndAndTickMisses=True";

            if (settings.NoDrain) yield return "NoDrain=True";
            if (settings.ReverseHP) yield return "ReverseHP=True";

            if (settings.GreatOffsetThresholdMs >= 0) yield return $"GreatOffsetThresholdMs={inv(settings.GreatOffsetThresholdMs)}";
            if (!float.IsNaN(settings.GreatOffsetPenaltyHP)) yield return $"GreatOffsetPenaltyHP={inv(settings.GreatOffsetPenaltyHP)}";

            if (settings.EnableDifficultyOverrides) yield return "EnableDifficultyOverrides=True";
            if (settings.AllowUnsafeDifficultyOverrideValues) yield return "AllowUnsafeDifficultyOverrideValues=True";
            if (settings.DifficultyOverrideStartWithBeatmapValues) yield return "DifficultyOverrideStartWithBeatmapValues=True";
            if (settings.EnableGradualDifficultyChange) yield return "EnableGradualDifficultyChange=True";
            if (!float.IsNaN(settings.GradualDifficultyChangeEndTimeMs)) yield return $"GradualDifficultyChangeEndTimeMs={inv(settings.GradualDifficultyChangeEndTimeMs)}";
            if (settings.KeepDifficultyOverridesAfterSection) yield return "KeepDifficultyOverridesAfterSection=True";
            if (!float.IsNaN(settings.SectionCircleSize)) yield return $"SectionCircleSize={inv(settings.SectionCircleSize)}";
            if (settings.EnableSectionCircleSizeWindow) yield return "EnableSectionCircleSizeWindow=True";
            if (settings.SectionCircleSizeStartTimeMs >= 0) yield return $"SectionCircleSizeStartTimeMs={inv(settings.SectionCircleSizeStartTimeMs)}";
            if (settings.SectionCircleSizeEndTimeMs >= 0) yield return $"SectionCircleSizeEndTimeMs={inv(settings.SectionCircleSizeEndTimeMs)}";
            if (settings.EnableGradualSectionCircleSizeChange) yield return "EnableGradualSectionCircleSizeChange=True";
            if (!float.IsNaN(settings.SectionApproachRate)) yield return $"SectionApproachRate={inv(settings.SectionApproachRate)}";
            if (settings.EnableSectionApproachRateWindow) yield return "EnableSectionApproachRateWindow=True";
            if (settings.SectionApproachRateStartTimeMs >= 0) yield return $"SectionApproachRateStartTimeMs={inv(settings.SectionApproachRateStartTimeMs)}";
            if (settings.SectionApproachRateEndTimeMs >= 0) yield return $"SectionApproachRateEndTimeMs={inv(settings.SectionApproachRateEndTimeMs)}";
            if (settings.EnableGradualSectionApproachRateChange) yield return "EnableGradualSectionApproachRateChange=True";
            if (!float.IsNaN(settings.SectionOverallDifficulty)) yield return $"SectionOverallDifficulty={inv(settings.SectionOverallDifficulty)}";
            if (settings.EnableSectionOverallDifficultyWindow) yield return "EnableSectionOverallDifficultyWindow=True";
            if (settings.SectionOverallDifficultyStartTimeMs >= 0) yield return $"SectionOverallDifficultyStartTimeMs={inv(settings.SectionOverallDifficultyStartTimeMs)}";
            if (settings.SectionOverallDifficultyEndTimeMs >= 0) yield return $"SectionOverallDifficultyEndTimeMs={inv(settings.SectionOverallDifficultyEndTimeMs)}";
            if (settings.EnableGradualSectionOverallDifficultyChange) yield return "EnableGradualSectionOverallDifficultyChange=True";
            if (settings.AllowUnsafeStackLeniencyOverrideValues) yield return "AllowUnsafeStackLeniencyOverrideValues=True";
            if (!float.IsNaN(settings.SectionStackLeniency)) yield return $"SectionStackLeniency={inv(settings.SectionStackLeniency)}";
            if (settings.AllowUnsafeTickRateOverrideValues) yield return "AllowUnsafeTickRateOverrideValues=True";
            if (!double.IsNaN(settings.SectionTickRate)) yield return $"SectionTickRate={inv(settings.SectionTickRate)}";
            if (settings.ForceHidden) yield return "ForceHidden=True";
            if (settings.ForceNoApproachCircle) yield return "ForceNoApproachCircle=True";
            if (settings.ForceHardRock) yield return "ForceHardRock=True";
            if (settings.ForceFlashlight) yield return "ForceFlashlight=True";
            if (settings.ForceTraceable) yield return "ForceTraceable=True";
            if (!float.IsNaN(settings.FlashlightRadius)) yield return $"FlashlightRadius={inv(settings.FlashlightRadius)}";
            if (settings.EnableGradualFlashlightRadiusChange) yield return "EnableGradualFlashlightRadiusChange=True";
            if (settings.EnableGradualFlashlightFadeIn) yield return "EnableGradualFlashlightFadeIn=True";
            if (!float.IsNaN(settings.GradualFlashlightRadiusEndTimeMs)) yield return $"GradualFlashlightRadiusEndTimeMs={inv(settings.GradualFlashlightRadiusEndTimeMs)}";
            if (settings.ForceDoubleTime) yield return "ForceDoubleTime=True";
            if (settings.ForceSingleTap) yield return "ForceSingleTap=True";
            if (settings.ForceAlternate) yield return "ForceAlternate=True";
            if (settings.ForceTransform) yield return "ForceTransform=True";
            if (settings.ForceWiggle) yield return "ForceWiggle=True";
            if (settings.ForceSpinIn) yield return "ForceSpinIn=True";
            if (settings.ForceGrow) yield return "ForceGrow=True";
            if (settings.ForceDeflate) yield return "ForceDeflate=True";
            if (settings.ForceBarrelRoll) yield return "ForceBarrelRoll=True";
            if (settings.ForceApproachDifferent) yield return "ForceApproachDifferent=True";
            if (settings.ForceMuted) yield return "ForceMuted=True";
            if (settings.ForceNoScope) yield return "ForceNoScope=True";
            if (settings.ForceMagnetised) yield return "ForceMagnetised=True";
            if (settings.ForceRepel) yield return "ForceRepel=True";
            if (settings.ForceFreezeFrame) yield return "ForceFreezeFrame=True";
            if (settings.ForceBubbles) yield return "ForceBubbles=True";
            if (settings.ForceSynesthesia) yield return "ForceSynesthesia=True";
            if (settings.ForceDepth) yield return "ForceDepth=True";
            if (settings.ForceBloom) yield return "ForceBloom=True";
            if (settings.WiggleStrength != 1.0f) yield return $"WiggleStrength={inv(settings.WiggleStrength)}";
            if (settings.GrowStartScale != 0.5f) yield return $"GrowStartScale={inv(settings.GrowStartScale)}";
            if (settings.DeflateStartScale != 2.0f) yield return $"DeflateStartScale={inv(settings.DeflateStartScale)}";
            if (settings.ApproachDifferentScale != 4.0f) yield return $"ApproachDifferentScale={inv(settings.ApproachDifferentScale)}";
            if (settings.NoScopeHiddenComboCount != 10) yield return $"NoScopeHiddenComboCount={settings.NoScopeHiddenComboCount}";
            if (settings.MagnetisedAttractionStrength != 0.5f) yield return $"MagnetisedAttractionStrength={inv(settings.MagnetisedAttractionStrength)}";
            if (settings.RepelRepulsionStrength != 0.5f) yield return $"RepelRepulsionStrength={inv(settings.RepelRepulsionStrength)}";
            if (settings.DepthMaxDepth != 100.0f) yield return $"DepthMaxDepth={inv(settings.DepthMaxDepth)}";
            if (settings.BloomMaxSizeComboCount != 50) yield return $"BloomMaxSizeComboCount={settings.BloomMaxSizeComboCount}";
            if (settings.BloomMaxCursorSize != 10.0f) yield return $"BloomMaxCursorSize={inv(settings.BloomMaxCursorSize)}";
            if (settings.BarrelRollSpinSpeed != 0.5) yield return $"BarrelRollSpinSpeed={inv(settings.BarrelRollSpinSpeed)}";
            if (settings.MutedMuteComboCount != 100) yield return $"MutedMuteComboCount={settings.MutedMuteComboCount}";

            if (!string.IsNullOrEmpty(settings.SectionName)) yield return $"SectionName={settings.SectionName}";

            if (settings.DisplayColor != Color4.White)
            {
                uint colorArgb = (uint)(settings.DisplayColor.A * 255);
                colorArgb |= (uint)(settings.DisplayColor.R * 255) << 8;
                colorArgb |= (uint)(settings.DisplayColor.G * 255) << 16;
                colorArgb |= (uint)(settings.DisplayColor.B * 255) << 24;
                yield return $"DisplayColor={colorArgb}";
            }
        }

        public static IEnumerable<string> SerialiseHitObjectSettings(HitObjectGimmickSettings settings)
        {
            if (settings.IsFakeNote) yield return "IsFakeNote=True";
            if (settings.FakePunishMode != FakePunishMode.None) yield return $"FakePunishMode={settings.FakePunishMode}";
            if (settings.FakePlayHitsound) yield return "FakePlayHitsound=True";
            if (settings.FakeAutoHitOnApproachClose) yield return "FakeAutoHitOnApproachClose=True";
            if (settings.FakeAutoHitPlayHitsound) yield return "FakeAutoHitPlayHitsound=True";
            if (!settings.FakeRevealEnabled) yield return "FakeRevealEnabled=False";
            if (Math.Abs(settings.FakeRevealRed - 1f) > 0.0001f) yield return $"FakeRevealRed={inv(settings.FakeRevealRed)}";
            if (Math.Abs(settings.FakeRevealGreen - 0.3019608f) > 0.0001f) yield return $"FakeRevealGreen={inv(settings.FakeRevealGreen)}";
            if (Math.Abs(settings.FakeRevealBlue - 0.3019608f) > 0.0001f) yield return $"FakeRevealBlue={inv(settings.FakeRevealBlue)}";
            if (Math.Abs(settings.FakeRevealStrength - HitObjectGimmickSettings.DEFAULT_FAKE_REVEAL_STRENGTH) > 0.0001f) yield return $"FakeRevealStrength={inv(settings.FakeRevealStrength)}";
            if (Math.Abs(settings.FakeRevealLeadInStartMs - HitObjectGimmickSettings.DEFAULT_FAKE_REVEAL_LEAD_IN_START_MS) > 0.0001f) yield return $"FakeRevealLeadInStartMs={inv(settings.FakeRevealLeadInStartMs)}";
            if (Math.Abs(settings.FakeRevealLeadInLengthMs - HitObjectGimmickSettings.DEFAULT_FAKE_REVEAL_LEAD_IN_LENGTH_MS) > 0.0001f) yield return $"FakeRevealLeadInLengthMs={inv(settings.FakeRevealLeadInLengthMs)}";
            if (Math.Abs(settings.FakeRevealFadeOutStartMs - HitObjectGimmickSettings.DEFAULT_FAKE_REVEAL_FADE_OUT_START_MS) > 0.0001f) yield return $"FakeRevealFadeOutStartMs={inv(settings.FakeRevealFadeOutStartMs)}";
            if (Math.Abs(settings.FakeRevealFadeOutLengthMs - HitObjectGimmickSettings.DEFAULT_FAKE_REVEAL_FADE_OUT_LENGTH_MS) > 0.0001f) yield return $"FakeRevealFadeOutLengthMs={inv(settings.FakeRevealFadeOutLengthMs)}";

            if (settings.EnableHPGimmick) yield return "EnableHPGimmick=True";
            if (settings.EnableNoMiss) yield return "EnableNoMiss=True";
            if (settings.EnableCountLimits) yield return "EnableCountLimits=True";
            if (settings.EnableGreatOffsetPenalty) yield return "EnableGreatOffsetPenalty=True";
            if (settings.Max300s >= 0) yield return $"Max300s={settings.Max300s}";
            if (settings.Max100s >= 0) yield return $"Max100s={settings.Max100s}";
            if (settings.Max50s >= 0) yield return $"Max50s={settings.Max50s}";
            if (settings.MaxMisses >= 0) yield return $"MaxMisses={settings.MaxMisses}";
            if (!float.IsNaN(settings.HP300)) yield return $"HP300={inv(settings.HP300)}";
            if (!float.IsNaN(settings.HP100)) yield return $"HP100={inv(settings.HP100)}";
            if (!float.IsNaN(settings.HP50)) yield return $"HP50={inv(settings.HP50)}";
            if (!float.IsNaN(settings.HPMiss)) yield return $"HPMiss={inv(settings.HPMiss)}";
            if (settings.GreatOffsetThresholdMs >= 0) yield return $"GreatOffsetThresholdMs={inv(settings.GreatOffsetThresholdMs)}";
            if (!float.IsNaN(settings.GreatOffsetPenaltyHP)) yield return $"GreatOffsetPenaltyHP={inv(settings.GreatOffsetPenaltyHP)}";

            if (settings.EnableDifficultyOverrides) yield return "EnableDifficultyOverrides=True";
            if (settings.AllowUnsafeDifficultyOverrideValues) yield return "AllowUnsafeDifficultyOverrideValues=True";
            if (!float.IsNaN(settings.SectionCircleSize)) yield return $"SectionCircleSize={inv(settings.SectionCircleSize)}";
            if (!float.IsNaN(settings.SectionApproachRate)) yield return $"SectionApproachRate={inv(settings.SectionApproachRate)}";
            if (!float.IsNaN(settings.SectionOverallDifficulty)) yield return $"SectionOverallDifficulty={inv(settings.SectionOverallDifficulty)}";
            if (settings.AllowUnsafeStackLeniencyOverrideValues) yield return "AllowUnsafeStackLeniencyOverrideValues=True";
            if (!float.IsNaN(settings.SectionStackLeniency)) yield return $"SectionStackLeniency={inv(settings.SectionStackLeniency)}";
            if (settings.AllowUnsafeTickRateOverrideValues) yield return "AllowUnsafeTickRateOverrideValues=True";
            if (!double.IsNaN(settings.SectionTickRate)) yield return $"SectionTickRate={inv(settings.SectionTickRate)}";
            if (settings.ForceHidden) yield return "ForceHidden=True";
            if (settings.ForceNoApproachCircle) yield return "ForceNoApproachCircle=True";
            if (settings.ForceHardRock) yield return "ForceHardRock=True";
            if (settings.ForceFlashlight) yield return "ForceFlashlight=True";
            if (settings.ForceTraceable) yield return "ForceTraceable=True";
            if (!float.IsNaN(settings.FlashlightRadius)) yield return $"FlashlightRadius={inv(settings.FlashlightRadius)}";
        }

        private static string inv(float value) => value.ToString(CultureInfo.InvariantCulture);

        private static string inv(double value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
