// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.MOsu.Utils;

namespace osu.Game.Rulesets.MOsu.Mods
{
    public class OsuModSpacingAdjust : Mod, IApplicableToBeatmap
    {
        public override string Name => "Spacing Adjust";

        public override LocalisableString Description => "Adjust object spacing to your liking.";


        public override string Acronym => "SA";

        public override ModType Type => ModType.Conversion;

        public override bool RequiresConfiguration => true;

        private readonly MosuHitObjectGenerationUtils.OriginalHitObjectStateStore originalState = new();

        [SettingSource("Object spacing", "Modifies the spacing between objects.")]
        public BindableNumber<float> ObjectSpacing { get; } = new BindableFloat
        {
            MinValue = 0.01f,
            MaxValue = 3,
            Default = 1,
            Value = 1,
            Precision = 0.01f,
        };

        // public override string SettingDescription => ObjectSpacing.IsDefault ? string.Empty : $"{ObjectSpacing.Value:N2}x";

        private const double min_break_duration = 1000;

        public void ApplyToBeatmap(IBeatmap beatmap)
        {
            if (!(beatmap is OsuBeatmap osuBeatmap))
                return;

            originalState.Restore(osuBeatmap);

            var positionInfos = MosuHitObjectGenerationUtils.GeneratePositionInfos(osuBeatmap.HitObjects);

            for (int i = 0; i < positionInfos.Count; i++)
            {
                var positionInfo = positionInfos[i];

                // if (i == 0 || positionInfos[i - 1].HitObject is Spinner ||
                //     positionInfo.HitObject.StartTime - positionInfos[i - 1].HitObject.GetEndTime() > min_break_duration)
                // {
                //     positionInfo.StayInPlace = true;
                // }

                if (ObjectSpacing.Value >= 1)
                {
                    // When increasing jump distance, longer jumps get a smaller increase in distance
                    positionInfo.DistanceFromPrevious *= MathF.Pow(ObjectSpacing.Value, 1f - positionInfo.DistanceFromPrevious / 640f);
                }
                else
                {
                    // When reducing jump distance, shorter jumps get a smaller reduction in distance
                    positionInfo.DistanceFromPrevious *= MathF.Pow(ObjectSpacing.Value, positionInfo.DistanceFromPrevious / 640f);
                }
            }

            osuBeatmap.HitObjects = MosuHitObjectGenerationUtils.RepositionHitObjectsClampOnly(positionInfos);
        }
    }
}
