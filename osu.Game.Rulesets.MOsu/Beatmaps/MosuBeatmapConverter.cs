// Converter producing MosuBeatmap so gimmick data has a home on the playable beatmap.
// Hitobject conversion itself is inherited from the stock OsuBeatmapConverter.

using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.MOsu.Gimmicks;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.MOsu.Edit;

namespace osu.Game.Rulesets.MOsu.Beatmaps
{
    public class MosuBeatmapConverter : OsuBeatmapConverter
    {
        public MosuBeatmapConverter(IBeatmap beatmap, Ruleset ruleset)
            : base(beatmap, ruleset)
        {
        }

        protected override Beatmap<OsuHitObject> CreateBeatmap() => new MosuBeatmap();

        protected override Beatmap<OsuHitObject> ConvertBeatmap(IBeatmap original, CancellationToken cancellationToken)
        {
            var beatmap = (MosuBeatmap)base.ConvertBeatmap(original, cancellationToken);

            // Carry pre-parsed gimmick data from a mosu source (e.g. the editor test injects
            // gimmicks via CreateBeatmap; the playable must keep them, as the test env's
            // working beatmap has no file access for the runtime to re-parse). In the real
            // game the source is a stock beatmap with no gimmicks and the runtime parses the
            // file itself, so this is a no-op there.
            if (original is MosuBeatmap mosuSource && mosuSource.Gimmicks.Parsed)
            {
                beatmap.Gimmicks = new MosuGimmickData
                {
                    Sections = MosuSectionGimmickEditorModel.CloneGimmicks(mosuSource.Gimmicks.Sections),
                    HitObjectGimmicks = new BeatmapHitObjectGimmicks
                    {
                        Entries = mosuSource.Gimmicks.HitObjectGimmicks.Entries.Select(MosuClipboardContent.CloneEntry).ToList(),
                    },
                    Parsed = mosuSource.Gimmicks.Parsed,
                };
            }

            return beatmap;
        }
    }
}
