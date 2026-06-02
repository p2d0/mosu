// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Overlays.Settings;
using osu.Game.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.MOsu.Mods
{
    public class OsuModMissChallenge : ModFailCondition, IApplicableToPlayer
    {
        public override string Name => "Miss Challenge";

        public override string Acronym => "MC";

        public override LocalisableString Description => "Fail if you miss too many objects!";

        public override ModType Type => ModType.DifficultyIncrease;

        public override double ScoreMultiplier => 1.0;

        public override Type[] IncompatibleMods => base.IncompatibleMods.Concat(new[] { typeof(ModPerfect) }).ToArray();

        public override bool RequiresConfiguration => false;

        public override bool Ranked => false;

        public override IEnumerable<(LocalisableString setting, LocalisableString value)> SettingDescription
        {
            get
            {
                if (UseBestLocalScore.Value)
                    yield return ("Use best local score", "On");

                if (!MaxMisses.IsDefault)
                    yield return ("Max misses", MaxMisses.Value.ToString());

                if (!Restart.IsDefault)
                    yield return ("Restart on fail", "On");
            }
        }

        [SettingSource("Use best local score", "Set max misses to one less than your best local score for this beatmap.")]
        public BindableBool UseBestLocalScore { get; } = new BindableBool();

        [SettingSource("Max misses", "Trigger a failure if your miss count exceeds this value.", SettingControlType = typeof(MissLimitSlider))]
        public BindableInt MaxMisses { get; } = new BindableInt
        {
            MinValue = 1,
            MaxValue = 100,
            Default = 10,
            Value = 10,
        };

        private int missCount;

        public void ApplyToPlayer(Player player)
        {
            if (!UseBestLocalScore.Value)
                return;

            var localUserManager = (LocalUserManager?)player.Dependencies.Get(typeof(LocalUserManager));
            if (localUserManager == null)
                return;

            var ruleset = player.Ruleset.Value;
            var beatmapHash = player.Beatmap.Value.BeatmapInfo.Hash;

            // Get the mod types active on this player (excluding Miss Challenge itself)
            var activeModTypes = player.Mods.Value
                .Select(m => m.GetType())
                .Where(t => t != typeof(OsuModMissChallenge) && t != typeof(ModNoFail))
                .OrderBy(t => t.Name)
                .ToList();

            var allScores = localUserManager.GetLocalScores(ruleset)
                .Where(s => s.BeatmapHash == beatmapHash)
                .Where(s =>
                {
                    var scoreModTypes = s.Mods
                        .Select(m => m.GetType())
                        .Where(t => t != typeof(OsuModMissChallenge) && t != typeof(ModNoFail))
                        .OrderBy(t => t.Name)
                        .ToList();
                    return scoreModTypes.SequenceEqual(activeModTypes);
                })
                .ToList();

            if (!allScores.Any())
                return;

            int minMisses = allScores.Min(s => s.Statistics.GetValueOrDefault(HitResult.Miss, int.MaxValue));
            int target = Math.Max(1, minMisses);
            MaxMisses.Value = Math.Min(target, MaxMisses.MaxValue);
        }

        protected override bool FailCondition(HealthProcessor healthProcessor, JudgementResult result)
        {
            if (result.Type == HitResult.Miss)
            {
                missCount++;
                if (missCount > MaxMisses.Value)
                    return true;
            }

            return false;
        }

        public override void ResetSettingsToDefaults()
        {
            base.ResetSettingsToDefaults();
            missCount = 0;
        }
    }

    public partial class MissLimitSlider : SettingsSlider<int>
    {
        protected override void LoadComplete()
        {
            base.LoadComplete();
            if (SettingSourceObject is OsuModMissChallenge mod)
            {
                mod.UseBestLocalScore.BindValueChanged(val =>
                {
                    if (val.NewValue) Hide(); else Show();
                }, true);
            }
        }
    }
}
