// MOsu's own playable beatmap type, carrying the parsed delta gimmick data so
// runtime components (drawable ruleset, health processor) never re-parse the file.

using osu.Game.Rulesets.MOsu.Gimmicks;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.MOsu.Beatmaps
{
    public class MosuBeatmap : OsuBeatmap
    {
        public MosuGimmickData Gimmicks { get; set; } = new MosuGimmickData();
    }
}
