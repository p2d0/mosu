// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using MongoDB.Bson;
using osu.Framework.Bindables;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.MOsu.Beatmaps;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.MOsu.Utils;
using osu.Framework.Logging;
using osuTK;
using osu.Framework.Graphics.Containers;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Game.Beatmaps.Timing;
using osu.Framework.Lists;
using Microsoft.Toolkit.HighPerformance.Helpers;

namespace osu.Game.Rulesets.MOsu.Mods
{
    public class OsuModSquare : Mod, IApplicableToBeatmap
    {

        public override string Name => "Square";
        public override string Acronym => "AQ";
        public override ModType Type => ModType.Conversion;
        public override double ScoreMultiplier => 1;
        public override LocalisableString Description => "Flip objects on the chosen axes.";
        public override Type[] IncompatibleMods => new[] { typeof(ModHardRock) };

        [SettingSource("Divisor", "Divisor selector")]
        public BindableInt Divisor { get; } = new BindableInt(2)
        {
            MinValue = 1,
            MaxValue = 16,
            Default = 2,
        };


        public void ApplyToBeatmap(IBeatmap beatmap)
        {
            makeMapSquare(beatmap);
        }

        // public void ApplyToBeatmap(IBeatmap beatmap)
        // {
        //     // The 'is' pattern matching already declares and assigns osuBeatmap if the cast is successful.
        //     if (beatmap is not OsuBeatmap osuBeatmap)
        //         return;

        //     var firstHitObject = beatmap.HitObjects.OfType<OsuHitObject>().FirstOrDefault();
        //     if (firstHitObject == null)
        //         return;

        //     // The original code had a redundant LastOrDefault check.
        //     var lastTime = beatmap.HitObjects.Last().StartTime;

        //     var firstTime = firstHitObject.StartTime;

        //     var hitObjects = new List<OsuHitObject>();

        //     osuBeatmap.Breaks.Clear();

        //     var beatLength = osuBeatmap.ControlPointInfo.TimingPointAt(firstTime).BeatLength / 2;

        //     // if (beatLength <= 0) // Add a safeguard against division by zero or invalid timing points
        //     //     beatLength = 200;

        //     var spacing = 200; // The side length of the square

        //     do
        //     {
        //         int cornerIndex = hitObjects.Count % 4;
        //         Vector2 position;

        //         // Determine the position based on which corner we are on
        //         switch (cornerIndex)
        //         {
        //             case 0: // Bottom-left
        //                 position = new Vector2(0, 0);
        //                 break;
        //             case 1: // Bottom-right
        //                 position = new Vector2(spacing, 0);
        //                 break;
        //             case 2: // Top-right
        //                 position = new Vector2(spacing, spacing);
        //                 break;
        //             case 3: // Top-left
        //                 position = new Vector2(0, spacing);
        //                 break;
        //             default: // This case will never be reached with % 4
        //                 position = Vector2.Zero;
        //                 break;
        //         }

        //         var circle = new HitCircle
        //         {
        //             StartTime = osuBeatmap.ControlPointInfo.GetClosestSnappedTime(firstTime + (beatLength * hitObjects.Count),2),
        //             Position = position,
        //             // Start a new combo at the beginning of each square
        //             NewCombo = cornerIndex == 0,
        //             Samples = firstHitObject.Samples
        //         };

        //         circle.ApplyDefaults(osuBeatmap.ControlPointInfo, osuBeatmap.Difficulty);
        //         hitObjects.Add(circle);
        //     } while (hitObjects.Last().StartTime < lastTime);

        //     osuBeatmap.HitObjects = hitObjects;
        // }

        private void makeMapSquare(IBeatmap beatmap){
            // The 'is' pattern matching already declares and assigns osuBeatmap if the cast is successful.

            if (beatmap is not OsuBeatmap osuBeatmap)
                return;

            // osuBeatmap.Breaks.Clear();


            var firstHitObject = beatmap.HitObjects.OfType<OsuHitObject>().FirstOrDefault();
            if (firstHitObject == null)
                return;

            // The original code had a redundant LastOrDefault check.
            var lastTime = beatmap.HitObjects.Last().StartTime;

            var firstTime = firstHitObject.StartTime;

            var hitObjects = new List<OsuHitObject>();


            var beatLength = osuBeatmap.ControlPointInfo.TimingPointAt(firstTime).BeatLength / Divisor.Value;

            // if (beatLength <= 0) // Add a safeguard against division by zero or invalid timing points
            //     beatLength = 200;

            var spacing = 200; // The side length of the square

            do
            {
                int cornerIndex = hitObjects.Count % 4;
                Vector2 position;

                // Determine the position based on which corner we are on
                switch (cornerIndex)
                {
                    case 0: // Bottom-left
                        position = new Vector2(0, 0);
                        break;
                    case 1: // Bottom-right
                        position = new Vector2(spacing, 0);
                        break;
                    case 2: // Top-right
                        position = new Vector2(spacing, spacing);
                        break;
                    case 3: // Top-left
                        position = new Vector2(0, spacing);
                        break;
                    default: // This case will never be reached with % 4
                        position = Vector2.Zero;
                        break;
                }

                var circle = new HitCircle
                {
                    Position = position,
                    // Start a new combo at the beginning of each square
                    NewCombo = cornerIndex == 0,
                    Samples = firstHitObject.Samples
                };

                circle.ApplyDefaults(osuBeatmap.ControlPointInfo, osuBeatmap.Difficulty);
                // circle.StartTime = firstTime + (beatLength * hitObjects.Count);
                circle.StartTime = osuBeatmap.ControlPointInfo.GetClosestSnappedTime(firstTime + (beatLength * hitObjects.Count));
                circle.TimePreempt = firstHitObject.TimePreempt;
                circle.TimeFadeIn = firstHitObject.TimeFadeIn;
                // if(osuBeatmap.ControlPointInfo.TimingPointAt(circle.StartTime).BeatLength / 2 > 5)
                //     beatLength = osuBeatmap.ControlPointInfo.TimingPointAt(circle.StartTime).BeatLength / 2;
                hitObjects.Add(circle);
            } while (hitObjects.Last().StartTime < lastTime);

            osuBeatmap.HitObjects = hitObjects;

            // osuBeatmap.Breaks.ForEach(b => new BreakPeriod(0,0));
            osuBeatmap.Breaks.Clear();
            // osuBeatmap.Breaks = new SortedList<BreakPeriod>(Comparer<BreakPeriod>.Default);
            Logger.Log($"Breaks: {beatmap.Breaks.Count()}");
            Logger.Log($"TotalBreakTime: {beatmap.TotalBreakTime}ms" );
        }


        // public void ApplyToHitObject(HitObject hitObject)
        // {
        //     var osuObject = (OsuHitObject)hitObject;
        //     osuObject.MaximumJudgementOffset

        // }

    }
}
