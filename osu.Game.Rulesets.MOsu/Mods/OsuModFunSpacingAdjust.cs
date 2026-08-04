// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
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
    public class OsuModFunSpacingAdjust : Mod, IApplicableToBeatmap
    {
        public override string Name => "Fun Spacing";

        public override LocalisableString Description => "Spacing grows/shrinks over time.";

        public override double ScoreMultiplier => 1;

        public override string Acronym => "FSA";

        public override ModType Type => ModType.Fun;

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

        [SettingSource("Starting spacing", "Spacing multiplier at the start of the beatmap.")]
        public BindableNumber<float> StartSpacing { get; } = new BindableFloat
        {
            MinValue = 0.1f,
            MaxValue = 3f,
            Default = 1f,
            Value = 1f,
            Precision = 0.01f,
        };

        [SettingSource("Ending spacing", "Spacing multiplier at the end of the beatmap.")]
        public BindableNumber<float> EndSpacing { get; } = new BindableFloat
        {
            MinValue = 0.1f,
            MaxValue = 3f,
            Default = 2f,
            Value = 2f,
            Precision = 0.01f,
        };

        public void ApplyToBeatmap(IBeatmap beatmap)
        {
            if (!(beatmap is OsuBeatmap osuBeatmap))
                return;

            restoreOriginals(osuBeatmap);

            var positionInfos = MosuHitObjectGenerationUtils.GeneratePositionInfos(osuBeatmap.HitObjects);

            double startTime = osuBeatmap.HitObjects[0]?.StartTime ?? 0;
            double endTime = osuBeatmap.HitObjects.LastOrDefault()?.GetEndTime() ?? 1;
            double duration = endTime - startTime;

            for (int i = 0; i < positionInfos.Count; i++)
            {
                var positionInfo = positionInfos[i];
                double progress = duration > 0 ? (positionInfo.HitObject.StartTime - startTime) / duration : 0;

                // Linear interpolation from start to end spacing
                float spacingMultiplier = StartSpacing.Value + (EndSpacing.Value - StartSpacing.Value) * (float)progress;

                if (spacingMultiplier >= 1)
                {
                    positionInfo.DistanceFromPrevious *= MathF.Pow(spacingMultiplier, 1f - positionInfo.DistanceFromPrevious / 640f);
                }
                else
                {
                    positionInfo.DistanceFromPrevious *= MathF.Pow(spacingMultiplier, positionInfo.DistanceFromPrevious / 640f);
                }
            }

            osuBeatmap.HitObjects = MosuHitObjectGenerationUtils.RepositionHitObjectsClampOnly(positionInfos);
        }
    }
}
