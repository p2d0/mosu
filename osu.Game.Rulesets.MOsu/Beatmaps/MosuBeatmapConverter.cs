// Converter producing MosuBeatmap so gimmick data has a home on the playable beatmap.
// Hitobject conversion itself is inherited from the stock OsuBeatmapConverter.

using osu.Game.Beatmaps;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Objects;

namespace osu.Game.Rulesets.MOsu.Beatmaps
{
    public class MosuBeatmapConverter : OsuBeatmapConverter
    {
        public MosuBeatmapConverter(IBeatmap beatmap, Ruleset ruleset)
            : base(beatmap, ruleset)
        {
        }

        protected override Beatmap<OsuHitObject> CreateBeatmap() => new MosuBeatmap();
    }
}
