// Gimmick data carrier for the playable beatmap.
//
// The delta fork stores section/hitobject gimmicks on the core beatmap (populated by
// its decoder). The NuGet decoder skips the gimmick sections entirely, so MOsu parses
// the .osu file per playable (see MosuGimmickRuntime) and stores the result on its own
// beatmap type (see MosuBeatmap).

namespace osu.Game.Rulesets.MOsu.Gimmicks
{
    public class MosuGimmickData
    {
        public BeatmapSectionGimmicks Sections { get; set; } = new BeatmapSectionGimmicks();

        public BeatmapHitObjectGimmicks HitObjectGimmicks { get; set; } = new BeatmapHitObjectGimmicks();

        /// <summary>
        /// Whether the .osu file has been parsed into this data.
        /// </summary>
        public bool Parsed { get; set; }

        /// <summary>
        /// Whether <see cref="MosuGimmickApplier"/> has already processed this beatmap instance.
        /// Guards against double-application when the same playable beatmap is played again.
        /// </summary>
        public bool Applied { get; set; }
    }
}
