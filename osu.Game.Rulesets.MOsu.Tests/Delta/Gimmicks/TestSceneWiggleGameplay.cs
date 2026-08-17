// Reproducer for "wiggle forced section does not apply in gameplay in the real game".
// Full real-game path: decode a .osu map (with a ForceWiggle section) -> convert to playable
// -> mount a real DrawableMosuRuleset -> runtime parses gimmicks from the working beatmap
// stream -> overlay hooks fire. Asserts an in-section drawable receives wiggle (MoveTo)
// transforms.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.MOsu.Delta.Beatmaps;using osu.Game.Rulesets.MOsu.Delta.Gimmicks;
using osu.Game.Rulesets.MOsu.Delta.Scoring;
using osu.Game.Rulesets.MOsu.UI;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Play;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Tests.Delta.Gimmicks
{
    public partial class TestSceneWiggleGameplay : TestSceneMOsuBase
    {
        private const double object_spacing = 1000;

        private bool dependenciesCached;
        private TestAdjustableClock testClock = null!;

        [Test]
        public void TestWiggleSectionAppliesInGameplay() => testSectionModAppliesInGameplay(ForceMod.Wiggle, false);

        [Test]
        public void TestTransformSectionAppliesToCircles() => testSectionModAppliesInGameplay(ForceMod.Transform, true);

        private enum ForceMod
        {
            Wiggle,
            Transform,
        }

        private void testSectionModAppliesInGameplay(ForceMod mod, bool includeSlider)
        {
            DrawableMosuRuleset drawableRuleset = null!;

            AddStep("build beatmap + ruleset", () =>
            {
                var settings = new SectionGimmickSettings();

                if (mod == ForceMod.Wiggle)
                {
                    settings.ForceWiggle = true;
                    settings.WiggleStrength = 1f;
                }
                else
                    settings.ForceTransform = true;

                var beatmap = new DeltaBeatmap
                {
                    BeatmapInfo = new BeatmapInfo { Ruleset = new MosuRuleset().RulesetInfo },
                    Gimmicks = new DeltaGimmickData
                    {
                        Parsed = true,
                        Sections = new BeatmapSectionGimmicks
                        {
                            Sections =
                            {
                                new SectionGimmickSection
                                {
                                    Id = 0,
                                    StartTime = 0,
                                    EndTime = -1, // unbounded
                                    Settings = settings,
                                },
                            },
                        },
                    },
                };

                for (int i = 0; i < 5; i++)
                {
                    var circle = new HitCircle
                    {
                        StartTime = 1000 + i * object_spacing,
                        Position = new Vector2(256, 192),
                    };
                    circle.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty());
                    beatmap.HitObjects.Add(circle);
                }

                if (includeSlider)
                {
                    var slider = new Slider
                    {
                        StartTime = 7000,
                        Position = new Vector2(256, 192),
                        Path = new SliderPath(PathType.LINEAR, new[] { Vector2.Zero, new Vector2(80, 0) }),
                    };
                    slider.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty());
                    beatmap.HitObjects.Add(slider);
                }

                drawableRuleset = new DrawableMosuRuleset(new MosuRuleset(), beatmap, Array.Empty<Mod>());

                // Player-provided gameplay scaffolding the ruleset's load expects.
                // (two [Test] methods share the scene instance, so only cache once)
                if (!dependenciesCached)
                {
                    dependenciesCached = true;
                    testClock = new TestAdjustableClock();
                    Dependencies.Cache(new Bindable<WorkingBeatmap> { Value = new FlatWorkingBeatmap(beatmap) });
                    Dependencies.Cache(new GameplayClockContainer(testClock, false, false));
                    Dependencies.Cache(new BeatmapDifficultyCache());

                    var scoreProcessor = new DeltaScoreProcessor();
                    scoreProcessor.ApplyBeatmap(beatmap);
                    Dependencies.Cache(scoreProcessor);
                }

                drawableRuleset.FrameStableComponents.Add(new BreakTracker(0, new DeltaScoreProcessor()));

                Add(drawableRuleset);
            });

            AddUntilStep("an in-section drawable alive", () =>
            {
                // advance the gameplay clock while polling (the scene clock also runs in real time)
                testClock.CurrentTime += 16;

                return drawableRuleset.Playfield.HitObjectContainer.AliveEntries.Any();
            });

            Vector2? firstPosition = null;

            AddUntilStep("circle position animates", () =>
            {
                var circleDrawable = drawableRuleset.Playfield.HitObjectContainer.AliveEntries
                    .Select(e => e.Value)
                    .FirstOrDefault(d => d.HitObject is HitCircle);

                if (circleDrawable == null)
                    return false;

                var position = circleDrawable.Position;

                if (firstPosition == null)
                {
                    firstPosition = position;
                    return false;
                }

                bool moved = Vector2.Distance(firstPosition.Value, position) > 1f;
                bool transformed = circleDrawable.Transforms.Any(t => t.TargetMember == "Position");
                Logger.Log($"[TEST] {mod} on hitcircle: drawable={circleDrawable.GetType().Name} start={circleDrawable.HitObject.StartTime} pos1={firstPosition.Value} pos2={position} transforms={circleDrawable.Transforms.Count()} moved={moved} transformed={transformed}");
                return moved && transformed;
            });

            if (includeSlider)
            {
                AddUntilStep("slider drawable alive", () =>
                    drawableRuleset.Playfield.HitObjectContainer.AliveEntries.Any(e => e.Value.HitObject is Slider));

                AddAssert("slider drawable receives position transforms", () =>
                {
                    var sliderDrawable = drawableRuleset.Playfield.HitObjectContainer.AliveEntries
                        .Select(e => e.Value).First(d => d.HitObject is Slider);

                    bool transformed = sliderDrawable.Transforms.Any(t => t.TargetMember == "Position");
                    Logger.Log($"[TEST] {mod} on slider: drawable={sliderDrawable.GetType().Name} start={sliderDrawable.HitObject.StartTime} transforms={sliderDrawable.Transforms.Count()} transformed={transformed}");
                    return transformed;
                });
            }
        }


        private class TestAdjustableClock : IAdjustableClock
        {
            public double CurrentTime { get; set; }

            public bool IsRunning { get; set; } = true;

            public double Rate { get; set; } = 1;

            public void Reset() => CurrentTime = 0;

            public void Start() => IsRunning = true;

            public void Stop() => IsRunning = false;

            public bool Seek(double position)
            {
                CurrentTime = position;
                return true;
            }

            public void ResetSpeedAdjustments()
            {
            }

            public void ProcessFrame()
            {
            }
        }
    }
}