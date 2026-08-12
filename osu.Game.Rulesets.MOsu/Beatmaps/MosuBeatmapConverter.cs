// Converter producing MosuBeatmap so gimmick data has a home on the playable beatmap,
// and MosuSlider so the removed 10x slider velocity cap (delta parity) applies to
// converted sliders.
// Hitobject conversion itself is inherited from the stock OsuBeatmapConverter.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Beatmaps.Legacy;
using osu.Game.Rulesets.MOsu.Edit;
using osu.Game.Rulesets.MOsu.Gimmicks;
using osu.Game.Rulesets.MOsu.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Beatmaps
{
    public class MosuBeatmapConverter : OsuBeatmapConverter
    {
        public MosuBeatmapConverter(IBeatmap beatmap, Ruleset ruleset)
            : base(beatmap, ruleset)
        {
        }

        protected override Beatmap<OsuHitObject> CreateBeatmap() => new MosuBeatmap();

        protected override IEnumerable<OsuHitObject> ConvertHitObject(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
        {
            var positionData = original as IHasPosition;
            var comboData = original as IHasCombo;
            var sliderVelocityData = original as IHasSliderVelocity;
            var generateTicksData = original as IHasGenerateTicks;

            switch (original)
            {
                case IHasPathWithRepeats curveData:
                    return new MosuSlider
                    {
                        StartTime = original.StartTime,
                        Samples = original.Samples,
                        Path = curveData.Path,
                        NodeSamples = curveData.NodeSamples,
                        RepeatCount = curveData.RepeatCount,
                        Position = positionData?.Position ?? Vector2.Zero,
                        NewCombo = comboData?.NewCombo ?? false,
                        ComboOffset = comboData?.ComboOffset ?? 0,
                        // prior to v8, speed multipliers don't adjust for how many ticks are generated over the same distance.
                        // this results in more (or less) ticks being generated in <v8 maps for the same time duration.
                        TickDistanceMultiplier = beatmap.BeatmapVersion < 8 ? 1f / ((LegacyControlPointInfo)beatmap.ControlPointInfo).DifficultyPointAt(original.StartTime).SliderVelocity : 1,
                        GenerateTicks = generateTicksData?.GenerateTicks ?? true,
                        SliderVelocityMultiplier = sliderVelocityData?.SliderVelocityMultiplier ?? 1,
                    }.Yield();

                case IHasDuration endTimeData:
                    return new Spinner
                    {
                        StartTime = original.StartTime,
                        Samples = original.Samples,
                        EndTime = endTimeData.EndTime,
                        Position = positionData?.Position ?? OsuPlayfield.BASE_SIZE / 2,
                        NewCombo = comboData?.NewCombo ?? false,
                        ComboOffset = comboData?.ComboOffset ?? 0,
                    }.Yield();

                default:
                    return new HitCircle
                    {
                        StartTime = original.StartTime,
                        Samples = original.Samples,
                        Position = positionData?.Position ?? Vector2.Zero,
                        NewCombo = comboData?.NewCombo ?? false,
                        ComboOffset = comboData?.ComboOffset ?? 0,
                    }.Yield();
            }
        }

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
