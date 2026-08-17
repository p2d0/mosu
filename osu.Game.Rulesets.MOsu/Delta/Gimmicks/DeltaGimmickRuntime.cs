// Shared gimmick runtime used by both the gameplay drawable ruleset and the editor ruleset:
// parses gimmicks once per map (cached), applies them to the playable beatmap, and creates
// gimmick drawable representations (fake notes, hidden/no-approach/traceable visuals).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.MOsu.Delta.Objects;
using osu.Game.Rulesets.MOsu.Delta.Objects.Drawables;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.MOsu.Delta.Gimmicks
{
    public static class DeltaGimmickRuntime
    {
        /// <summary>
        /// Fired after gimmicks have been parsed + applied to a playable beatmap, so a score
        /// processor that simulated max stats before the parse can re-simulate.
        /// </summary>
        public static event Action? GimmicksApplied;

        /// <summary>
        /// Parses the delta gimmick sections (skipped by the stock decoder) and applies them to the
        /// playable beatmap, if not already done. The .osu file is parsed once per map (cached).
        /// </summary>
        public static void EnsureApplied(IBeatmap playableBeatmap, WorkingBeatmap? workingBeatmap, bool mutateList = true)
        {
            if (playableBeatmap is not Beatmaps.DeltaBeatmap mosuBeatmap)
            {
                Logger.Log($"[MOsu] gimmicks skipped: playable beatmap is {playableBeatmap.GetType().Name}");
                return;
            }

            var data = mosuBeatmap.Gimmicks;

            Dictionary<double, double>? sliderVelocities = null;

            if (!data.Parsed)
            {
                data.Parsed = true;

                var path = workingBeatmap?.BeatmapInfo.Path;

                if (workingBeatmap == null || string.IsNullOrEmpty(path))
                {
                    Logger.Log("[MOsu] gimmicks skipped: no working beatmap or path");
                    return;
                }

                var storagePath = workingBeatmap.BeatmapInfo.BeatmapSet?.GetPathForFile(path);

                // After a MOsu save the realm Path may lag the actual file name; the working
                // beatmap can still resolve the stream itself.
                using var stream = storagePath != null
                    ? workingBeatmap.GetStream(storagePath)
                    : workingBeatmap.GetStream(path);

                if (stream == null)
                {
                    Logger.Log($"[MOsu] gimmicks skipped: stream null for {path}");
                    return;
                }

                using var reader = new StreamReader(stream);
                string text = reader.ReadToEnd();

                (data.Sections, data.HitObjectGimmicks) = DeltaGimmickParser.Parse(new StringReader(text));
                sliderVelocities = DeltaGimmickParser.ParseSliderVelocity(new StringReader(text));
                Logger.Log($"[MOsu] parsed gimmicks: {data.Sections.Sections.Count} sections, {data.HitObjectGimmicks.Entries.Count} hitobject entries, {sliderVelocities.Count} SV points");
            }

            // Delta removes the 10x slider velocity cap in core; replicate from the ruleset by
            // re-applying the raw SV (the stock decoder clamps it) and recomputing velocity.
            // Bindables are always widened; the raw re-apply + velocity recompute only run when
            // the file was parsed (SV data present).
            applyUncappedSliderVelocity(mosuBeatmap, sliderVelocities);

            if (data.Sections.Sections.Count == 0 && data.HitObjectGimmicks.Entries.Count == 0)
            {
                Logger.Log("[MOsu] gimmicks skipped: no entries after parse");
                return;
            }

            Logger.Log($"[MOsu] Applying {data.Sections.Sections.Count} section gimmicks and {data.HitObjectGimmicks.Entries.Count} hitobject gimmicks");
            DeltaGimmickApplier.Apply(mosuBeatmap, data, mutateList);
            GimmicksApplied?.Invoke();
        }

        /// <summary>
        /// Re-applies raw slider velocities from the file (the decoder clamped them to 10x) and
        /// widens the velocity bindables so the uncapped values survive. Runs before the gimmick
        /// apply so section difficulty overrides land on top of the recomputed defaults.
        /// </summary>
        private static void applyUncappedSliderVelocity(Beatmaps.DeltaBeatmap mosuBeatmap, Dictionary<double, double>? sliderVelocities)
        {
            bool any = false;

            foreach (var controlPoint in mosuBeatmap.ControlPointInfo.AllControlPoints.OfType<DifficultyControlPoint>())
            {
                controlPoint.SliderVelocityBindable.MinValue = 0;
                controlPoint.SliderVelocityBindable.MaxValue = 1000;

                if (sliderVelocities != null && sliderVelocities.TryGetValue(controlPoint.Time, out double sv))
                {
                    controlPoint.SliderVelocity = sv;
                    any = true;
                }
            }

            if (sliderVelocities == null || sliderVelocities.Count == 0)
                return;

            foreach (var slider in mosuBeatmap.HitObjects.OfType<Slider>())
            {
                DeltaSlider.UncapVelocityBindable(slider);

                double sv = rawSliderVelocityAt(sliderVelocities, slider.StartTime);

                if (sv > 0)
                {
                    slider.SliderVelocityMultiplier = sv;
                    any = true;
                }

                // Recompute velocity/tick distance/nested objects with the uncapped formula
                // (DeltaSlider.ApplyDefaultsToSelf) instead of the stock 10x-capped one.
                slider.ApplyDefaults(mosuBeatmap.ControlPointInfo, mosuBeatmap.Difficulty);
            }

            Logger.Log($"[MOsu] applied uncapped slider velocity (points={sliderVelocities.Count} touched={any})");
        }

        private static double rawSliderVelocityAt(Dictionary<double, double> sliderVelocities, double time)
        {
            double bestTime = double.NegativeInfinity;
            double bestSv = 0;

            foreach (var (pointTime, sv) in sliderVelocities)
            {
                if (pointTime <= time && pointTime > bestTime)
                {
                    bestTime = pointTime;
                    bestSv = sv;
                }
            }

            return bestSv;
        }

        /// <summary>
        /// Creates the drawable representation for a gimmicked hitobject (fake notes,
        /// forced hidden / no-approach-circle / traceable), or null for a stock drawable.
        /// Must be called after <see cref="EnsureApplied"/>.
        /// </summary>
        public static DrawableHitObject<OsuHitObject>? CreateGimmickDrawableRepresentation(IBeatmap playableBeatmap, OsuHitObject h)
        {
            if (playableBeatmap is not Beatmaps.DeltaBeatmap mosuBeatmap)
                return null;

            var data = mosuBeatmap.Gimmicks;

            if (data.HitObjectGimmicks.Entries.Count == 0 && data.Sections.Sections.Count == 0)
                return null;

            // The apply step replaced fake sources in the playable's HitObjects list; these
            // objects are already fakes, so wrap them directly.
            switch (h)
            {
                case FakeHitCircle fakeCircle:
                    return new DrawableFakeHitCircle(fakeCircle);

                case FakeSlider fakeSlider:
                    return new DrawableFakeSlider(fakeSlider);
            }

            var objectSettings = DeltaGimmickApplier.GetObjectSettings(playableBeatmap, data, h);

            if (objectSettings?.IsFakeNote == true
                && DeltaGimmickApplier.CreateFakeObject(playableBeatmap, data, h) is OsuHitObject fakeObject)
            {
                fakeObject.ApplyDefaults(playableBeatmap.ControlPointInfo, DeltaGimmickApplier.ResolveDifficultyForObject(playableBeatmap, data, h));
                DeltaGimmickApplier.ApplyForcedModsToObject(playableBeatmap, data, fakeObject);

                return fakeObject switch
                {
                    FakeHitCircle fakeCircle => new DrawableFakeHitCircle(fakeCircle),
                    FakeSlider fakeSlider => new DrawableFakeSlider(fakeSlider),
                    _ => null
                };
            }

            var section = data.Sections.FindSectionAt(h.StartTime);
            bool hidden = section?.Settings.ForceHidden == true || objectSettings?.ForceHidden == true;
            bool noApproach = section?.Settings.ForceNoApproachCircle == true || objectSettings?.ForceNoApproachCircle == true;
            bool traceable = section?.Settings.ForceTraceable == true || objectSettings?.ForceTraceable == true;

            if (hidden || noApproach || traceable)
            {
                return h switch
                {
                    HitCircle circle => new MosuDrawableHitCircle(circle, hidden, noApproach, traceable),
                    Slider slider => new MosuDrawableSlider(slider, hidden, noApproach, traceable),
                    Spinner spinner => new MosuDrawableSpinner(spinner, hidden, noApproach, traceable),
                    _ => null
                };
            }

            return null;
        }
    }
}
