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

namespace osu.Game.Rulesets.MOsu.Mods
{
    public class OsuModMissChallenge : ModFailCondition
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
                if (!MaxMisses.IsDefault)
                    yield return ("Max misses", MaxMisses.Value.ToString());

                if (!Restart.IsDefault)
                    yield return ("Restart on fail", "On");
            }
        }

        [SettingSource("Max misses", "Trigger a failure if your miss count exceeds this value.")]
        public BindableInt MaxMisses { get; } = new BindableInt
        {
            MinValue = 1,
            MaxValue = 100,
            Default = 10,
            Value = 10,
        };

        private int missCount;

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
}
