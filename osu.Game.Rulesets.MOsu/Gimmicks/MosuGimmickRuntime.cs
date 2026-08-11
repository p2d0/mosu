// Shared gimmick runtime used by both the gameplay drawable ruleset and the editor ruleset:
// parses gimmicks once per map (cached), applies them to the playable beatmap, and creates
// gimmick drawable representations (fake notes, hidden/no-approach/traceable visuals).

using System.IO;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.MOsu.Objects;
using osu.Game.Rulesets.MOsu.Objects.Drawables;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.MOsu.Gimmicks
{
    public static class MosuGimmickRuntime
    {
        /// <summary>
        /// Parses the delta gimmick sections (skipped by the stock decoder) and applies them to the
        /// playable beatmap, if not already done. The .osu file is parsed once per map (cached).
        /// </summary>
        public static void EnsureApplied(IBeatmap playableBeatmap, WorkingBeatmap? workingBeatmap)
        {
            if (playableBeatmap is not Beatmaps.MosuBeatmap mosuBeatmap)
                return;

            var data = mosuBeatmap.Gimmicks;

            if (!data.Parsed)
            {
                data.Parsed = true;

                string cacheKey = $"{mosuBeatmap.BeatmapInfo.OnlineID}:{mosuBeatmap.BeatmapInfo.MD5Hash}:{mosuBeatmap.BeatmapInfo.Path}";
                var cached = MosuGimmickCache.TryGet(cacheKey);

                if (cached != null)
                {
                    data = mosuBeatmap.Gimmicks = cached;
                }
                else
                {
                    var path = workingBeatmap?.BeatmapInfo.Path;

                    if (workingBeatmap == null || string.IsNullOrEmpty(path))
                        return;

                    var storagePath = workingBeatmap.BeatmapInfo.BeatmapSet?.GetPathForFile(path);
                    if (storagePath == null)
                        return;

                    using var stream = workingBeatmap.GetStream(storagePath);
                    if (stream == null)
                        return;

                    using var reader = new StreamReader(stream);
                    (data.Sections, data.HitObjectGimmicks) = MosuGimmickParser.Parse(reader);
                    MosuGimmickCache.Set(cacheKey, data);
                }
            }

            if (data.Sections.Sections.Count == 0 && data.HitObjectGimmicks.Entries.Count == 0)
                return;

            Logger.Log($"[MOsu] Applying {data.Sections.Sections.Count} section gimmicks and {data.HitObjectGimmicks.Entries.Count} hitobject gimmicks");
            MosuGimmickApplier.Apply(mosuBeatmap, data);
        }

        /// <summary>
        /// Creates the drawable representation for a gimmicked hitobject (fake notes,
        /// forced hidden / no-approach-circle / traceable), or null for a stock drawable.
        /// Must be called after <see cref="EnsureApplied"/>.
        /// </summary>
        public static DrawableHitObject<OsuHitObject>? CreateGimmickDrawableRepresentation(IBeatmap playableBeatmap, OsuHitObject h)
        {
            if (playableBeatmap is not Beatmaps.MosuBeatmap mosuBeatmap)
                return null;

            var data = mosuBeatmap.Gimmicks;

            if (data.HitObjectGimmicks.Entries.Count == 0 && data.Sections.Sections.Count == 0)
                return null;

            var objectSettings = MosuGimmickApplier.GetObjectSettings(playableBeatmap, data, h);

            if (objectSettings?.IsFakeNote == true
                && MosuGimmickApplier.CreateFakeObject(playableBeatmap, data, h) is OsuHitObject fakeObject)
            {
                fakeObject.ApplyDefaults(playableBeatmap.ControlPointInfo, MosuGimmickApplier.ResolveDifficultyForObject(playableBeatmap, data, h));
                MosuGimmickApplier.ApplyForcedModsToObject(playableBeatmap, data, fakeObject);

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
