// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.MOsu.Beatmaps;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.MOsu.Utils;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Mods
{
    public class OsuModSpacingAdjust : Mod, IApplicableToBeatmap
    {
        public override string Name => "Spacing Adjust";

        public override LocalisableString Description => "Adjust object spacing to your liking.";

        public override double ScoreMultiplier => 1;

        public override string Acronym => "SA";

        public override ModType Type => ModType.Conversion;

        public override bool RequiresConfiguration => true;

        private ConditionalWeakTable<OsuHitObject, object> originalPositions = new();
        private ConditionalWeakTable<Slider, object> originalSliderPaths = new();

        private void restoreOriginals(OsuBeatmap osuBeatmap)
        {
            foreach (var obj in osuBeatmap.HitObjects.OfType<OsuHitObject>())
            {
                if (!originalPositions.TryGetValue(obj, out var pos))
                {
                    pos = obj.Position;
                    originalPositions.Add(obj, pos);
                }
                obj.Position = (Vector2)pos;

                if (obj is Slider slider)
                {
                    if (!originalSliderPaths.TryGetValue(slider, out var pathState))
                    {
                        pathState = slider.Path.ControlPoints.Select(p => p.Position).ToArray();
                        originalSliderPaths.Add(slider, pathState);
                    }
                    var pts = (Vector2[])pathState;
                    for (int j = 0; j < slider.Path.ControlPoints.Count && j < pts.Length; j++)
                        slider.Path.ControlPoints[j].Position = pts[j];
                }
            }
        }

        [SettingSource("Object spacing", "Modifies the spacing between objects.")]
        public BindableNumber<float> ObjectSpacing { get; } = new BindableFloat
        {
            MinValue = 0.1f,
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

            restoreOriginals(osuBeatmap);

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
