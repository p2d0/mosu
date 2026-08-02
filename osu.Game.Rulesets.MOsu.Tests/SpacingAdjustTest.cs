using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.MOsu.Mods;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Tests
{
    /// <summary>
    /// Verifies OsuModSpacingAdjust moves objects smoothly as the spacing value grows,
    /// with no discontinuity between 1.00 (unchanged map) and 1.001.
    /// </summary>
    [TestFixture]
    public class SpacingAdjustTest
    {
        private const int object_count = 60;

        private static OsuBeatmap createBeatmap()
        {
            var controlPointInfo = new ControlPointInfo();
            controlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });

            var beatmap = new OsuBeatmap
            {
                BeatmapInfo = new BeatmapInfo { Difficulty = new BeatmapDifficulty { ApproachRate = 8 } },
                StackLeniency = 0,
                ControlPointInfo = controlPointInfo
            };

            // Fan of objects out from the centre towards every edge. Distances stay well inside the
            // (512x384) MOsu playfield so nothing gets clamped at spacing 1.0, while objects near the
            // border still exercise the edge-away rotation.
            var rng = new Random(1234);
            for (int i = 0; i < object_count; i++)
            {
                double angle = i / (double)object_count * Math.PI * 2;
                float dist = 30 + rng.Next(130);
                beatmap.HitObjects.Add(new HitCircle
                {
                    StartTime = i * 50,
                    Position = OsuPlayfield.BASE_SIZE / 2 + new Vector2((float)Math.Cos(angle) * dist, (float)Math.Sin(angle) * dist),
                    NewCombo = i % 8 == 0
                });
            }

            return beatmap;
        }

        /// <summary>
        /// Applies the mod to <paramref name="beatmap"/> at the given spacing, reusing a single
        /// mod instance across calls so the mod can restore true original positions each time
        /// (mirrors in-game usage).
        /// </summary>
        private sealed class SpacingApplier
        {
            private readonly OsuBeatmap beatmap;
            private readonly OsuModSpacingAdjust mod = new OsuModSpacingAdjust();

            public SpacingApplier(OsuBeatmap beatmap) => this.beatmap = beatmap;

            public List<Vector2> Apply(float spacing)
            {
                mod.ObjectSpacing.Value = spacing;
                mod.ApplyToBeatmap(beatmap);
                return beatmap.HitObjects.OfType<HitCircle>().Select(c => c.Position).ToList();
            }
        }

        private static float maxDisplacement(IEnumerable<Vector2> a, IEnumerable<Vector2> b) =>
            a.Zip(b).Select(p => Vector2.Distance(p.First, p.Second)).Max();

        [Test]
        public void Spacing1Point0PreservesOriginalPositions()
        {
            var beatmap = createBeatmap();
            var original = beatmap.HitObjects.OfType<HitCircle>().Select(c => c.Position).ToList();

            var applier = new SpacingApplier(beatmap);
            var at1 = applier.Apply(1f);

            Assert.That(at1, Is.EqualTo(original));
        }

        [Test]
        public void SpacingJustAbove1Point0MovesSmoothly()
        {
            var beatmap = createBeatmap();
            var applier = new SpacingApplier(beatmap);
            var at1 = applier.Apply(1f);

            // A 0.1% spacing increase must not displace any object by more than a couple of pixels.
            var at1001 = applier.Apply(1.001f);
            float jump = maxDisplacement(at1, at1001);
            TestContext.Progress.WriteLine($"[SpacingAdjustTest] max displacement 1.00 -> 1.001 = {jump}px");
            Assert.That(jump, Is.LessThan(2f), $"Objects jumped {jump}px when spacing went from 1.00 to 1.001");

            // And each subsequent small step keeps moving smoothly.
            float prev = 1.001f;
            var prevPos = at1001;
            for (float spacing = 1.002f; spacing <= 1.01f; spacing += 0.001f)
            {
                var pos = applier.Apply(spacing);
                float step = maxDisplacement(prevPos, pos);
                TestContext.Progress.WriteLine($"[SpacingAdjustTest] max displacement {prev} -> {spacing} = {step}px");
                Assert.That(step, Is.LessThan(2f), $"Objects jumped {step}px when spacing went from {prev} to {spacing}");
                prev = spacing;
                prevPos = pos;
            }
        }

        [Test]
        public void SpacingIncreaseIsMonotonic()
        {
            var beatmap = createBeatmap();
            var applier = new SpacingApplier(beatmap);
            var at1 = applier.Apply(1f);
            var at2 = applier.Apply(2f);

            // Every object should move at least a little when spacing doubles.
            float minMove = at1.Zip(at2).Select(p => Vector2.Distance(p.First, p.Second)).Min();
            TestContext.Progress.WriteLine($"[SpacingAdjustTest] min displacement 1.00 -> 2.00 = {minMove}px");
            Assert.That(minMove, Is.GreaterThan(1f));
        }
    }
}
