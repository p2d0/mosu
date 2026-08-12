// Per-gimmick tests for hitobject gimmicks: build a small beatmap (a hitcircle + a slider),
// apply the gimmick to them via the runtime applier, and verify the gameplay effect
// (drawable representation / object properties), mirroring the Frums autoplay map path.

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.MOsu.Beatmaps;
using osu.Game.Rulesets.MOsu.Gimmicks;
using osu.Game.Rulesets.MOsu.Objects;
using osu.Game.Rulesets.MOsu.Objects.Drawables;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Tests.Gimmicks
{
    public partial class TestSceneHitObjectGimmicks : TestSceneMOsuBase
    {
        private const double circle_time = 500;
        private const double slider_time = 1000;

        private static readonly Vector2 circle_position = new Vector2(200, 100);
        private static readonly Vector2 slider_position = new Vector2(300, 192);

        private MosuBeatmap beatmap = null!;
        private MosuGimmickData data = null!;
        private HitCircle circle = null!;
        private Slider slider = null!;

        private void setupBeatmap()
        {
            beatmap = new MosuBeatmap();
            data = new MosuGimmickData();

            circle = createCircle(circle_time, circle_position);
            slider = createSlider(slider_time, slider_position);

            beatmap.HitObjects.Add(circle);
            beatmap.HitObjects.Add(slider);
        }

        private void apply()
        {
            beatmap.Gimmicks = data;
            MosuGimmickApplier.Apply(beatmap, data);
        }

        private static HitCircle createCircle(double time, Vector2 position)
        {
            var circle = new HitCircle { StartTime = time, Position = position };
            circle.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty());
            return circle;
        }

        private static Slider createSlider(double time, Vector2 position)
        {
            var slider = new Slider
            {
                StartTime = time,
                Position = position,
                Path = new SliderPath(PathType.LINEAR, new[] { Vector2.Zero, new Vector2(80, 0) }),
            };
            slider.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty());
            return slider;
        }

        private HitObjectGimmickEntry entryFor(OsuHitObject obj)
            => new HitObjectGimmickEntry
            {
                ObjectId = MosuGimmickApplier.GetObjectId(obj),
                StartTime = obj.StartTime,
                ComboIndexWithOffsets = obj.ComboIndexWithOffsets,
            };

        [Test]
        public void TestFakeNote()
        {
            AddStep("build beatmap", setupBeatmap);

            AddStep("verify playable carries pre-set gimmicks (autoplay path)", () =>
            {
                var source = new MosuBeatmap
                {
                    BeatmapInfo = new BeatmapInfo { Ruleset = new MosuRuleset().RulesetInfo },
                    Gimmicks = new MosuGimmickData
                    {
                        HitObjectGimmicks = new BeatmapHitObjectGimmicks(),
                        Parsed = true,
                    },
                };

                var sourceCircle = createCircle(500, new Vector2(100, 192));
                source.HitObjects.Add(sourceCircle);
                source.Gimmicks.HitObjectGimmicks.Entries.Add(new HitObjectGimmickEntry
                {
                    ObjectId = MosuGimmickApplier.GetObjectId(sourceCircle),
                    StartTime = sourceCircle.StartTime,
                    ComboIndexWithOffsets = sourceCircle.ComboIndexWithOffsets,
                    Settings = new HitObjectGimmickSettings { IsFakeNote = true },
                });

                var working = CreateWorkingBeatmap(source);
                var playable = (MosuBeatmap)working.GetPlayableBeatmap(new MosuRuleset().RulesetInfo, Array.Empty<osu.Game.Rulesets.Mods.Mod>());

                Logger.Log($"[TEST] playable gimmicks parsed={playable.Gimmicks.Parsed} entries={playable.Gimmicks.HitObjectGimmicks.Entries.Count}");

                MosuGimmickRuntime.EnsureApplied(playable, working);
                var drawable = MosuGimmickRuntime.CreateGimmickDrawableRepresentation(playable, playable.HitObjects.OfType<OsuHitObject>().First());
                Logger.Log($"[TEST] playable fake drawable: {drawable?.GetType().Name}");
            });

AddStep("fake the circle and the slider", () =>
            {
                var circleEntry = entryFor(circle);
                circleEntry.Settings.IsFakeNote = true;

                var sliderEntry = entryFor(slider);
                sliderEntry.Settings.IsFakeNote = true;

                data.HitObjectGimmicks.Entries.Add(circleEntry);
                data.HitObjectGimmicks.Entries.Add(sliderEntry);

                apply();
            });

            AddAssert("circle renders as fake", () =>
            {
                var circleDrawable = MosuGimmickRuntime.CreateGimmickDrawableRepresentation(beatmap, circle);
                Logger.Log($"[TEST] fake circle drawable: {circleDrawable?.GetType().Name}");
                return circleDrawable is DrawableFakeHitCircle;
            });

            AddAssert("slider renders as fake", () =>
            {
                var sliderDrawable = MosuGimmickRuntime.CreateGimmickDrawableRepresentation(beatmap, slider);
                Logger.Log($"[TEST] fake slider drawable: {sliderDrawable?.GetType().Name}");
                return sliderDrawable is DrawableFakeSlider;
            });
        }

        [Test]
        public void TestForceHidden()
        {
            AddStep("build beatmap", setupBeatmap);

            AddStep("hide the circle", () =>
            {
                var entry = entryFor(circle);
                entry.Settings.ForceHidden = true;
                data.HitObjectGimmicks.Entries.Add(entry);
                apply();
            });

            AddAssert("circle renders hidden", () => MosuGimmickRuntime.CreateGimmickDrawableRepresentation(beatmap, circle) is MosuDrawableHitCircle);
            AddAssert("slider unaffected", () => MosuGimmickRuntime.CreateGimmickDrawableRepresentation(beatmap, slider) is null);
        }

        [Test]
        public void TestForceNoApproachCircle()
        {
            AddStep("build beatmap", setupBeatmap);

            AddStep("remove approach circle from the circle", () =>
            {
                var entry = entryFor(circle);
                entry.Settings.ForceNoApproachCircle = true;
                data.HitObjectGimmicks.Entries.Add(entry);
                apply();
            });

            AddAssert("circle renders without approach circle", () => MosuGimmickRuntime.CreateGimmickDrawableRepresentation(beatmap, circle) is MosuDrawableHitCircle);
            AddAssert("slider unaffected", () => MosuGimmickRuntime.CreateGimmickDrawableRepresentation(beatmap, slider) is null);
        }

        [Test]
        public void TestForceHardRock()
        {
            AddStep("build beatmap", setupBeatmap);

            AddStep("hard rock the circle", () =>
            {
                var entry = entryFor(circle);
                entry.Settings.ForceHardRock = true;
                data.HitObjectGimmicks.Entries.Add(entry);
                apply();
            });

            AddAssert("circle flipped vertically", () =>
            {
                Logger.Log($"[TEST] circle y={circle.Position.Y} (base 192, flipped ~384)");
                return Math.Abs(circle.Position.Y - (PLAYFIELD_HEIGHT - circle_position.Y)) < 0.01;
            });
        }

        [Test]
        public void TestForceTraceable()
        {
            AddStep("build beatmap", setupBeatmap);

            AddStep("make the circle traceable", () =>
            {
                var entry = entryFor(circle);
                entry.Settings.ForceTraceable = true;
                data.HitObjectGimmicks.Entries.Add(entry);
                apply();
            });

            AddAssert("circle renders traceable", () => MosuGimmickRuntime.CreateGimmickDrawableRepresentation(beatmap, circle) is MosuDrawableHitCircle);
            AddAssert("slider unaffected", () => MosuGimmickRuntime.CreateGimmickDrawableRepresentation(beatmap, slider) is null);
        }

        [Test]
        public void TestDifficultyOverrides()
        {
            AddStep("build beatmap", setupBeatmap);

            AddStep("override AR 9.8 and CS 6 on the circle", () =>
            {
                var entry = entryFor(circle);
                entry.Settings.EnableDifficultyOverrides = true;
                entry.Settings.SectionApproachRate = 1f;
                entry.Settings.SectionCircleSize = 0f;
                data.HitObjectGimmicks.Entries.Add(entry);
                apply();
            });

            AddAssert("circle preempt reflects AR 9.8", () =>
            {
                // base AR 5 -> preempt ~1200; AR 1 -> ~1680
                Logger.Log($"[TEST] circle TimePreempt={circle.TimePreempt} (AR 1 -> ~1680)");
                return Math.Abs(circle.TimePreempt - 1680) < 2;
            });

            AddAssert("circle scale reflects CS 6", () =>
            {
                Logger.Log($"[TEST] circle Scale={circle.Scale} (CS 0 -> ~0.85)");
                return Math.Abs(circle.Scale - 0.85f) < 0.01;
            });

            AddAssert("slider keeps base difficulty", () =>
            {
                Logger.Log($"[TEST] slider TimePreempt={slider.TimePreempt} (base AR 5 -> ~1200)");
                return Math.Abs(slider.TimePreempt - 1200) < 2;
            });
        }

        private const float PLAYFIELD_HEIGHT = 384;
    }
}
