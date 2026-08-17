// Parses the delta fork's custom .osu gimmick sections, which the stock
// (NuGet) LegacyBeatmapDecoder does not understand and silently skips:
//
//   [BeatmapSectionGimmicks]
//   id,startTime,endTime,Key=Value|Key=Value...
//
//   [BeatmapHitObjectGimmicks]
//   startTime,comboIndexWithOffsets,Key=Value|Key=Value...
//
// Format ported from delta fork (osu.Game/Beatmaps/Formats/LegacyBeatmapDecoder.cs
// handleSectionGimmick / handleHitObjectGimmick).
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using osuTK.Graphics;

namespace osu.Game.Rulesets.MOsu.Delta.Gimmicks
{
    public static class DeltaGimmickParser
    {
        public static (BeatmapSectionGimmicks Sections, BeatmapHitObjectGimmicks HitObjects) Parse(TextReader reader)
        {
            var sections = new BeatmapSectionGimmicks();
            var hitObjectGimmicks = new BeatmapHitObjectGimmicks();

            string? section = null;
            string? line;

            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();

                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    section = line[1..^1];
                    continue;
                }

                switch (section)
                {
                    case "BeatmapSectionGimmicks":
                        handleSectionGimmick(line, sections);
                        break;

                    case "BeatmapHitObjectGimmicks":
                        handleHitObjectGimmick(line, hitObjectGimmicks);
                        break;
                }
            }

            dedupeHitObjectEntries(hitObjectGimmicks);
            return (sections, hitObjectGimmicks);
        }

        /// <summary>
        /// Parses the raw slider velocity multipliers from <c>[TimingPoints]</c>, keyed by time.
        /// The stock decoder clamps SV to 10x during decode, so the raw values are re-read here
        /// and re-applied by the runtime (delta removes the 10x cap in core; mosu cannot).
        /// </summary>
        public static Dictionary<double, double> ParseSliderVelocity(TextReader reader)
        {
            var result = new Dictionary<double, double>();

            bool inTimingPoints = false;
            string? line;

            while ((line = reader.ReadLine()) != null)
            {
                line = line.Trim();

                if (line.Length == 0 || line.StartsWith("//", StringComparison.Ordinal))
                    continue;

                if (line.StartsWith('[') && line.EndsWith(']'))
                {
                    inTimingPoints = line[1..^1] == "TimingPoints";
                    continue;
                }

                if (!inTimingPoints)
                    continue;

                string[] split = line.Split(',');
                if (split.Length < 2)
                    continue;

                if (!double.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double time))
                    continue;

                if (!double.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double beatLength))
                    continue;

                if (beatLength < 0)
                    result[time] = 100.0 / -beatLength;
            }

            return result;
        }

        private static void dedupeHitObjectEntries(BeatmapHitObjectGimmicks gimmicks)
        {
            var seen = new HashSet<(double StartTime, int ComboIndexWithOffsets)>();

            for (int i = gimmicks.Entries.Count - 1; i >= 0; i--)
            {
                var e = gimmicks.Entries[i];

                if (!seen.Add((e.StartTime, e.ComboIndexWithOffsets)))
                    gimmicks.Entries.RemoveAt(i);
            }
        }

        private static void handleSectionGimmick(string line, BeatmapSectionGimmicks sections)
        {
            string[] split = line.Split(',', 4);
            if (split.Length < 3)
                return;

            if (!int.TryParse(split[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                return;

            if (!double.TryParse(split[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double startTime))
                return;

            if (!double.TryParse(split[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double endTime))
                return;

            var section = new SectionGimmickSection
            {
                Id = id,
                StartTime = startTime,
                EndTime = endTime,
                Settings = new SectionGimmickSettings()
            };

            if (split.Length == 4 && !string.IsNullOrEmpty(split[3]))
                parseKeyValues(split[3], (key, value) => applySectionKeyValue(section.Settings, key, value));

            sections.Sections.Add(section);
        }

        private static void handleHitObjectGimmick(string line, BeatmapHitObjectGimmicks gimmicks)
        {
            string[] split = line.Split(',', 3);
            if (split.Length < 2)
                return;

            if (!double.TryParse(split[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double startTime))
                return;

            if (!int.TryParse(split[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int comboIndexWithOffsets))
                return;

            var entry = new HitObjectGimmickEntry
            {
                StartTime = startTime,
                ComboIndexWithOffsets = comboIndexWithOffsets,
                Settings = new HitObjectGimmickSettings(),
            };

            if (split.Length == 3 && !string.IsNullOrEmpty(split[2]))
            {
                foreach (string kv in split[2].Split('|'))
                {
                    if (string.IsNullOrEmpty(kv))
                        continue;

                    var pair = splitKeyVal(kv, '=');

                    if (pair.Key == "ObjectId")
                    {
                        if (long.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long objectId))
                            entry.ObjectId = objectId;
                        continue;
                    }

                    if (pair.Key == "Index")
                    {
                        if (int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                            entry.HitObjectIndex = index;
                        continue;
                    }

                    if (pair.Key == "Index")
                    {
                        if (int.TryParse(pair.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
                            entry.HitObjectIndex = index;
                        continue;
                    }

                    applyHitObjectKeyValue(entry.Settings, pair.Key, pair.Value);
                }
            }

            SectionGimmickValueClamper.ClampHitObjectSettingsInPlace(entry.Settings);

            gimmicks.Entries.Add(entry);
        }

        private static void parseKeyValues(string payload, Action<string, string> apply)
        {
            foreach (string kv in payload.Split('|'))
            {
                if (string.IsNullOrEmpty(kv))
                    continue;

                var pair = splitKeyVal(kv, '=');
                if (string.IsNullOrEmpty(pair.Key))
                    continue;

                apply(pair.Key, pair.Value);
            }
        }

        private static (string Key, string Value) splitKeyVal(string kv, char separator)
        {
            int index = kv.IndexOf(separator);
            if (index < 0)
                return (kv, string.Empty);

            return (kv.Substring(0, index), kv.Substring(index + 1));
        }

        private static void applySectionKeyValue(SectionGimmickSettings settings, string key, string value)
        {
            switch (key)
            {
                case "EnableHPGimmick": settings.EnableHPGimmick = parseBool(value); break;
                case "EnableNoMiss": settings.EnableNoMiss = parseBool(value); break;
                case "EnableAccuracyRequirement": settings.EnableAccuracyRequirement = parseBool(value); break;
                case "RequiredAccuracy": settings.RequiredAccuracy = parseFloat(value); break;
                case "EnableCountLimits": settings.EnableCountLimits = parseBool(value); break;
                case "EnableNoMissedSliderEnd": settings.EnableNoMissedSliderEnd = parseBool(value); break;
                case "EnableGreatOffsetPenalty": settings.EnableGreatOffsetPenalty = parseBool(value); break;
                case "Max300s": settings.Max300s = parseInt(value); break;
                case "Max100s": settings.Max100s = parseInt(value); break;
                case "Max50s": settings.Max50s = parseInt(value); break;
                case "MaxMisses": settings.MaxMisses = parseInt(value); break;
                case "Max300sAffectsSliderEndsAndTicks": settings.Max300sAffectsSliderEndsAndTicks = parseBool(value); break;
                case "Max100sAffectsSliderEndsAndTicks": settings.Max100sAffectsSliderEndsAndTicks = parseBool(value); break;
                case "Max50sAffectsSliderEndsAndTicks": settings.Max50sAffectsSliderEndsAndTicks = parseBool(value); break;
                case "MaxMissesAffectsSliderEndAndTickMisses": settings.MaxMissesAffectsSliderEndAndTickMisses = parseBool(value); break;
                case "HP300": settings.HP300 = parseFloat(value); break;
                case "HP100": settings.HP100 = parseFloat(value); break;
                case "HP50": settings.HP50 = parseFloat(value); break;
                case "HPMiss": settings.HPMiss = parseFloat(value); break;
                case "HPStart": settings.HPStart = parseFloat(value); break;
                case "HPCap": settings.HPCap = parseFloat(value); break;
                case "HP300AffectsSliderEndsAndTicks": settings.HP300AffectsSliderEndsAndTicks = parseBool(value); break;
                case "HP100AffectsSliderEndsAndTicks": settings.HP100AffectsSliderEndsAndTicks = parseBool(value); break;
                case "HP50AffectsSliderEndsAndTicks": settings.HP50AffectsSliderEndsAndTicks = parseBool(value); break;
                case "HPMissAffectsSliderEndAndTickMisses": settings.HPMissAffectsSliderEndAndTickMisses = parseBool(value); break;
                case "NoDrain": settings.NoDrain = parseBool(value); break;
                case "ReverseHP": settings.ReverseHP = parseBool(value); break;
                case "GreatOffsetThresholdMs": settings.GreatOffsetThresholdMs = parseFloat(value); break;
                case "GreatOffsetPenaltyHP": settings.GreatOffsetPenaltyHP = parseFloat(value); break;
                case "EnableDifficultyOverrides": settings.EnableDifficultyOverrides = parseBool(value); break;
                case "AllowUnsafeDifficultyOverrideValues": settings.AllowUnsafeDifficultyOverrideValues = parseBool(value); break;
                case "DifficultyOverrideStartWithBeatmapValues": settings.DifficultyOverrideStartWithBeatmapValues = parseBool(value); break;
                case "EnableGradualDifficultyChange": settings.EnableGradualDifficultyChange = parseBool(value); break;
                case "GradualDifficultyChangeEndTimeMs": settings.GradualDifficultyChangeEndTimeMs = parseFloat(value); break;
                case "KeepDifficultyOverridesAfterSection": settings.KeepDifficultyOverridesAfterSection = parseBool(value); break;
                case "SectionCircleSize": settings.SectionCircleSize = parseFloat(value); break;
                case "EnableSectionCircleSizeWindow": settings.EnableSectionCircleSizeWindow = parseBool(value); break;
                case "SectionCircleSizeStartTimeMs": settings.SectionCircleSizeStartTimeMs = parseFloat(value); break;
                case "SectionCircleSizeEndTimeMs": settings.SectionCircleSizeEndTimeMs = parseFloat(value); break;
                case "EnableGradualSectionCircleSizeChange": settings.EnableGradualSectionCircleSizeChange = parseBool(value); break;
                case "SectionApproachRate": settings.SectionApproachRate = parseFloat(value); break;
                case "EnableSectionApproachRateWindow": settings.EnableSectionApproachRateWindow = parseBool(value); break;
                case "SectionApproachRateStartTimeMs": settings.SectionApproachRateStartTimeMs = parseFloat(value); break;
                case "SectionApproachRateEndTimeMs": settings.SectionApproachRateEndTimeMs = parseFloat(value); break;
                case "EnableGradualSectionApproachRateChange": settings.EnableGradualSectionApproachRateChange = parseBool(value); break;
                case "SectionOverallDifficulty": settings.SectionOverallDifficulty = parseFloat(value); break;
                case "EnableSectionOverallDifficultyWindow": settings.EnableSectionOverallDifficultyWindow = parseBool(value); break;
                case "SectionOverallDifficultyStartTimeMs": settings.SectionOverallDifficultyStartTimeMs = parseFloat(value); break;
                case "SectionOverallDifficultyEndTimeMs": settings.SectionOverallDifficultyEndTimeMs = parseFloat(value); break;
                case "EnableGradualSectionOverallDifficultyChange": settings.EnableGradualSectionOverallDifficultyChange = parseBool(value); break;
                case "AllowUnsafeStackLeniencyOverrideValues": settings.AllowUnsafeStackLeniencyOverrideValues = parseBool(value); break;
                case "SectionStackLeniency": settings.SectionStackLeniency = parseFloat(value); break;
                case "AllowUnsafeTickRateOverrideValues": settings.AllowUnsafeTickRateOverrideValues = parseBool(value); break;
                case "SectionTickRate": settings.SectionTickRate = parseDouble(value); break;
                case "ForceHidden": settings.ForceHidden = parseBool(value); break;
                case "ForceNoApproachCircle": settings.ForceNoApproachCircle = parseBool(value); break;
                case "ForceHardRock": settings.ForceHardRock = parseBool(value); break;
                case "ForceFlashlight": settings.ForceFlashlight = parseBool(value); break;
                case "ForceTraceable": settings.ForceTraceable = parseBool(value); break;
                case "FlashlightRadius": settings.FlashlightRadius = parseFloat(value); break;
                case "EnableGradualFlashlightRadiusChange": settings.EnableGradualFlashlightRadiusChange = parseBool(value); break;
                case "EnableGradualFlashlightFadeIn": settings.EnableGradualFlashlightFadeIn = parseBool(value); break;
                case "GradualFlashlightRadiusEndTimeMs": settings.GradualFlashlightRadiusEndTimeMs = parseFloat(value); break;
                case "ForceDoubleTime": settings.ForceDoubleTime = parseBool(value); break;
                case "ForceSingleTap": settings.ForceSingleTap = parseBool(value); break;
                case "ForceAlternate": settings.ForceAlternate = parseBool(value); break;
                case "ForceTransform": settings.ForceTransform = parseBool(value); break;
                case "ForceWiggle": settings.ForceWiggle = parseBool(value); break;
                case "ForceSpinIn": settings.ForceSpinIn = parseBool(value); break;
                case "ForceGrow": settings.ForceGrow = parseBool(value); break;
                case "ForceDeflate": settings.ForceDeflate = parseBool(value); break;
                case "ForceBarrelRoll": settings.ForceBarrelRoll = parseBool(value); break;
                case "ForceApproachDifferent": settings.ForceApproachDifferent = parseBool(value); break;
                case "ForceMuted": settings.ForceMuted = parseBool(value); break;
                case "ForceNoScope": settings.ForceNoScope = parseBool(value); break;
                case "ForceMagnetised": settings.ForceMagnetised = parseBool(value); break;
                case "ForceRepel": settings.ForceRepel = parseBool(value); break;
                case "ForceFreezeFrame": settings.ForceFreezeFrame = parseBool(value); break;
                case "ForceBubbles": settings.ForceBubbles = parseBool(value); break;
                case "ForceSynesthesia": settings.ForceSynesthesia = parseBool(value); break;
                case "ForceDepth": settings.ForceDepth = parseBool(value); break;
                case "ForceBloom": settings.ForceBloom = parseBool(value); break;
                case "WiggleStrength": settings.WiggleStrength = parseFloat(value); break;
                case "GrowStartScale": settings.GrowStartScale = parseFloat(value); break;
                case "DeflateStartScale": settings.DeflateStartScale = parseFloat(value); break;
                case "ApproachDifferentScale": settings.ApproachDifferentScale = parseFloat(value); break;
                case "NoScopeHiddenComboCount": settings.NoScopeHiddenComboCount = parseInt(value); break;
                case "MagnetisedAttractionStrength": settings.MagnetisedAttractionStrength = parseFloat(value); break;
                case "RepelRepulsionStrength": settings.RepelRepulsionStrength = parseFloat(value); break;
                case "DepthMaxDepth": settings.DepthMaxDepth = parseFloat(value); break;
                case "BloomMaxSizeComboCount": settings.BloomMaxSizeComboCount = parseInt(value); break;
                case "BloomMaxCursorSize": settings.BloomMaxCursorSize = parseFloat(value); break;
                case "BarrelRollSpinSpeed": settings.BarrelRollSpinSpeed = parseDouble(value); break;
                case "MutedMuteComboCount": settings.MutedMuteComboCount = parseInt(value); break;
                case "SectionName": settings.SectionName = value; break;
                case "DisplayColor":
                    if (uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint colorArgb))
                    {
                        float a = (colorArgb & 0xFF) / 255f;
                        float r = ((colorArgb >> 8) & 0xFF) / 255f;
                        float g = ((colorArgb >> 16) & 0xFF) / 255f;
                        float b = ((colorArgb >> 24) & 0xFF) / 255f;
                        settings.DisplayColor = new Color4(r, g, b, a);
                    }
                    break;
            }
        }

        private static void applyHitObjectKeyValue(HitObjectGimmickSettings settings, string key, string value)
        {
            switch (key)
            {
                case "EnableHPGimmick": settings.EnableHPGimmick = parseBool(value); break;
                case "IsFakeNote": settings.IsFakeNote = parseBool(value); break;
                case "FakePunishMode":
                    if (!Enum.TryParse(value, true, out FakePunishMode fakePunishMode))
                        fakePunishMode = FakePunishMode.None;

                    if (fakePunishMode != FakePunishMode.None && fakePunishMode != FakePunishMode.Miss)
                        fakePunishMode = FakePunishMode.Miss;

                    settings.FakePunishMode = fakePunishMode;
                    break;
                case "FakePlayHitsound": settings.FakePlayHitsound = parseBool(value); break;
                case "FakeAutoHitOnApproachClose": settings.FakeAutoHitOnApproachClose = parseBool(value); break;
                case "FakeAutoHitPlayHitsound": settings.FakeAutoHitPlayHitsound = parseBool(value); break;
                case "FakeRevealEnabled": settings.FakeRevealEnabled = parseBool(value); break;
                case "FakeRevealRed": settings.FakeRevealRed = parseFloat(value); break;
                case "FakeRevealGreen": settings.FakeRevealGreen = parseFloat(value); break;
                case "FakeRevealBlue": settings.FakeRevealBlue = parseFloat(value); break;
                case "FakeRevealStrength": settings.FakeRevealStrength = parseFloat(value); break;
                case "FakeRevealLeadInStartMs": settings.FakeRevealLeadInStartMs = parseFloat(value); break;
                case "FakeRevealLeadInLengthMs": settings.FakeRevealLeadInLengthMs = parseFloat(value); break;
                case "FakeRevealFadeOutStartMs": settings.FakeRevealFadeOutStartMs = parseFloat(value); break;
                case "FakeRevealFadeOutLengthMs": settings.FakeRevealFadeOutLengthMs = parseFloat(value); break;
                case "EnableNoMiss": settings.EnableNoMiss = parseBool(value); break;
                case "EnableCountLimits": settings.EnableCountLimits = parseBool(value); break;
                case "EnableGreatOffsetPenalty": settings.EnableGreatOffsetPenalty = parseBool(value); break;
                case "Max300s": settings.Max300s = parseInt(value); break;
                case "Max100s": settings.Max100s = parseInt(value); break;
                case "Max50s": settings.Max50s = parseInt(value); break;
                case "MaxMisses": settings.MaxMisses = parseInt(value); break;
                case "HP300": settings.HP300 = parseFloat(value); break;
                case "HP100": settings.HP100 = parseFloat(value); break;
                case "HP50": settings.HP50 = parseFloat(value); break;
                case "HPMiss": settings.HPMiss = parseFloat(value); break;
                case "GreatOffsetThresholdMs": settings.GreatOffsetThresholdMs = parseFloat(value); break;
                case "GreatOffsetPenaltyHP": settings.GreatOffsetPenaltyHP = parseFloat(value); break;
                case "EnableDifficultyOverrides": settings.EnableDifficultyOverrides = parseBool(value); break;
                case "AllowUnsafeDifficultyOverrideValues": settings.AllowUnsafeDifficultyOverrideValues = parseBool(value); break;
                case "SectionCircleSize": settings.SectionCircleSize = parseFloat(value); break;
                case "SectionApproachRate": settings.SectionApproachRate = parseFloat(value); break;
                case "SectionOverallDifficulty": settings.SectionOverallDifficulty = parseFloat(value); break;
                case "AllowUnsafeStackLeniencyOverrideValues": settings.AllowUnsafeStackLeniencyOverrideValues = parseBool(value); break;
                case "SectionStackLeniency": settings.SectionStackLeniency = parseFloat(value); break;
                case "AllowUnsafeTickRateOverrideValues": settings.AllowUnsafeTickRateOverrideValues = parseBool(value); break;
                case "SectionTickRate": settings.SectionTickRate = parseDouble(value); break;
                case "ForceHidden": settings.ForceHidden = parseBool(value); break;
                case "ForceNoApproachCircle": settings.ForceNoApproachCircle = parseBool(value); break;
                case "ForceHardRock": settings.ForceHardRock = parseBool(value); break;
                case "ForceFlashlight": settings.ForceFlashlight = parseBool(value); break;
                case "ForceTraceable": settings.ForceTraceable = parseBool(value); break;
                case "FlashlightRadius": settings.FlashlightRadius = parseFloat(value); break;
            }
        }

        private static bool parseBool(string boolValue)
            => boolValue == "1" || boolValue.Equals("true", StringComparison.OrdinalIgnoreCase);

        private static int parseInt(string value)
            => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : default;

        private static float parseFloat(string value)
            => float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : float.NaN;

        private static double parseDouble(string value)
            => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : double.NaN;
    }
}
