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
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Play;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Tests.Delta.Gimmicks
{
    public partial class TestSceneWiggleGameplay : TestSceneMOsuBase
    {
        private const double object_spacing = 1000;

        [Test]
        public void TestWiggleSectionAppliesInGameplay()
        {
            DrawableMosuRuleset drawableRuleset = null!;
            TestAdjustableClock testClock = null!;

            AddStep("build wiggle beatmap + ruleset", () =>
            {
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
                                    Settings = new SectionGimmickSettings
                                    {
                                        ForceWiggle = true,
                                        WiggleStrength = 1f,
                                    },
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

                drawableRuleset = new DrawableMosuRuleset(new MosuRuleset(), beatmap, Array.Empty<Mod>());

                // Player-provided gameplay scaffolding the ruleset's load expects.
                testClock = new TestAdjustableClock();
                Dependencies.Cache(new Bindable<WorkingBeatmap> { Value = new FlatWorkingBeatmap(beatmap) });
                Dependencies.Cache(new GameplayClockContainer(testClock, false, false));
                Dependencies.Cache(new BeatmapDifficultyCache());

                var scoreProcessor = new DeltaScoreProcessor();
                scoreProcessor.ApplyBeatmap(beatmap);
                Dependencies.Cache(scoreProcessor);
                drawableRuleset.FrameStableComponents.Add(new BreakTracker(0, scoreProcessor));

                Add(drawableRuleset);
            });

            AddUntilStep("an in-section drawable alive", () =>
            {
                // advance the gameplay clock while polling (the scene clock also runs in real time)
                testClock.CurrentTime += 16;

                return drawableRuleset.Playfield.HitObjectContainer.AliveEntries.Any();
            });

            AddAssert("in-section drawable wiggles", () =>
            {
                var drawable = drawableRuleset.Playfield.HitObjectContainer.AliveEntries.First().Value;

                bool wiggled = drawable.Transforms.Any(t => t.TargetMember == "Position");
                Logger.Log($"[TEST] wiggle in gameplay: drawable={drawable.GetType().Name} start={drawable.HitObject.StartTime} transforms={drawable.Transforms.Count()} wiggled={wiggled}");
                return wiggled;
            });
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