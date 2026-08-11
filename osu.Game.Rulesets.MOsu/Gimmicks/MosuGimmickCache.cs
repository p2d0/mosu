// Gimmick data carrier for the playable beatmap, plus a session-level parse cache.
//
// The delta fork stores section/hitobject gimmicks on the core beatmap (populated by
// its decoder). The NuGet decoder skips the gimmick sections entirely, so MOsu parses
// the .osu file once per map (cached by file identity) and stores the result on its
// own beatmap type (see MosuBeatmap), avoiding a per-play reparse.

using System.Collections.Generic;

namespace osu.Game.Rulesets.MOsu.Gimmicks
{
    public class MosuGimmickData
    {
        public BeatmapSectionGimmicks Sections { get; set; } = new BeatmapSectionGimmicks();

        public BeatmapHitObjectGimmicks HitObjectGimmicks { get; set; } = new BeatmapHitObjectGimmicks();

        /// <summary>
        /// Whether the .osu file has been parsed into this data (once per map, cached).
        /// </summary>
        public bool Parsed { get; set; }

        /// <summary>
        /// Whether <see cref="MosuGimmickApplier"/> has already processed this beatmap instance.
        /// Guards against double-application when the same playable beatmap is played again.
        /// </summary>
        public bool Applied { get; set; }
    }

    public static class MosuGimmickCache
    {
        private static readonly Dictionary<string, MosuGimmickData> cache = new Dictionary<string, MosuGimmickData>();

        public static MosuGimmickData? TryGet(string key)
        {
            lock (cache)
            {
                return cache.TryGetValue(key, out var data) ? data : null;
            }
        }

        public static void Set(string key, MosuGimmickData data)
        {
            lock (cache)
            {
                cache[key] = data;
            }
        }
    }
}
