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
            {
                Logger.Log($"[MOsu] gimmicks skipped: playable beatmap is {playableBeatmap.GetType().Name}");
                return;
            }

            var data = mosuBeatmap.Gimmicks;

            if (!data.Parsed)
            {
                data.Parsed = true;

                string cacheKey = $"{mosuBeatmap.BeatmapInfo.OnlineID}:{mosuBeatmap.BeatmapInfo.MD5Hash}:{mosuBeatmap.BeatmapInfo.Path}";
                var cached = MosuGimmickCache.TryGet(cacheKey);

                if (cached != null)
                {
                    data = mosuBeatmap.Gimmicks = cached;

                    // The cached data is shared across beatmap instances, but Applied is per-beatmap:
                    // this beatmap's objects still need the gimmicks applied.
                    data.Applied = false;

                    Logger.Log($"[MOsu] gimmick cache hit: {cacheKey}");
                }
                else
                {
                    var path = workingBeatmap?.BeatmapInfo.Path;
                    Logger.Log($"[MOsu] gimmick cache miss: {cacheKey} working={workingBeatmap?.GetType().Name} path={path}");

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
                    (data.Sections, data.HitObjectGimmicks) = MosuGimmickParser.Parse(reader);
                    MosuGimmickCache.Set(cacheKey, data);
                    Logger.Log($"[MOsu] parsed gimmicks: {data.Sections.Sections.Count} sections, {data.HitObjectGimmicks.Entries.Count} hitobject entries");
                }
            }

            if (data.Sections.Sections.Count == 0 && data.HitObjectGimmicks.Entries.Count == 0)
            {
                Logger.Log("[MOsu] gimmicks skipped: no entries after parse");
                return;
            }

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
