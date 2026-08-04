using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.MOsu.Utils;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Tests
{
    /// <summary>
    /// Verifies <see cref="MosuHitObjectGenerationUtils.OriginalHitObjectStateStore"/> records original
    /// positions and slider paths on first sighting and restores them on subsequent calls — the invariant
    /// the position-editing mods (RandomV2, SpacingAdjust, FunSpacingAdjust) rely on for repeatable re-application.
    /// </summary>
    [TestFixture]
    public class OriginalStateStoreTest
    {
        private static OsuBeatmap createBeatmap()
        {
            var beatmap = new OsuBeatmap
            {
                BeatmapInfo = new BeatmapInfo { Difficulty = new BeatmapDifficulty { ApproachRate = 8 } },
                StackLeniency = 0,
                ControlPointInfo = new ControlPointInfo(),
            };

            beatmap.HitObjects.Add(new HitCircle { StartTime = 0, Position = new Vector2(100, 100) });
            beatmap.HitObjects.Add(new Slider
            {
                StartTime = 500,
                Position = new Vector2(200, 200),
                Path = new SliderPath(new[]
                {
                    new PathControlPoint(Vector2.Zero, PathType.LINEAR),
                    new PathControlPoint(new Vector2(80, 0), PathType.LINEAR),
                })
            });

            return beatmap;
        }

        [Test]
        public void RestoresPositionsAndSliderPathsAfterMutation()
        {
            var beatmap = createBeatmap();
            var store = new MosuHitObjectGenerationUtils.OriginalHitObjectStateStore();
            var circle = (HitCircle)beatmap.HitObjects[0];
            var slider = (Slider)beatmap.HitObjects[1];

            // First sighting records the original state.
            store.Restore(beatmap);

            // Mutate positions and slider path, as a mod pass would.
            circle.Position = new Vector2(400, 300);
            slider.Position = new Vector2(50, 50);
            slider.Path.ControlPoints[1].Position = new Vector2(200, 100);

            store.Restore(beatmap);

            Assert.That(circle.Position, Is.EqualTo(new Vector2(100, 100)));
            Assert.That(slider.Position, Is.EqualTo(new Vector2(200, 200)));
            Assert.That(slider.Path.ControlPoints[1].Position, Is.EqualTo(new Vector2(80, 0)));
        }
    }
}
