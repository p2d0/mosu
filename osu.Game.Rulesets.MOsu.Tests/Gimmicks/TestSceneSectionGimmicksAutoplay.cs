// Visual autoplay tests for section gimmicks — one gimmick per test case, 15 objects
// (10 hitcircles + 5 sliders interleaved) with one bounded section covering the middle of the
// map. Each test plays its map with autoplay until the section is on screen, then ends so the
// runner screenshots the result. No assertions — verification is visual.
//
// PlayerTestScene-derived scenes are excluded from the automated --auto runner; run manually
// (test browser, or --filter without --auto).

using System;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.MOsu.Beatmaps;
using osu.Game.Rulesets.MOsu.Gimmicks;
using osu.Game.Rulesets.MOsu.Tests.Screens;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Tests.Gimmicks
{
    public partial class TestSceneSectionGimmicksAutoplay : TestSceneOsuPlayer
    {
        protected override bool HasCustomSteps => true;

        private const int object_count = 15;
        private const double start_time = 1000;
        private const double object_spacing = 500;
        private const double section_start = 2500;
        private const double section_end = 5500;

        private void loadPlayer(Func<RulesetInfo, MosuBeatmap> build)
        {
            var osuRuleset = new MosuRuleset();
            Ruleset.Value = osuRuleset.RulesetInfo;
            Beatmap.Value = CreateWorkingBeatmap(build(osuRuleset.RulesetInfo));
            SelectedMods.Value = new Mod[] { osuRuleset.GetAutoplayMod()! };
            Player = CreatePlayer(osuRuleset)!;
            LoadScreen(Player);
        }

        /// <summary>
        /// Builds 15 objects (10 hitcircles + 5 sliders interleaved, every 500ms) with one
        /// bounded section covering the middle (2500-5500), then runs <paramref name="configure"/>
        /// over the section's settings.
        /// </summary>
        private MosuBeatmap buildMap(RulesetInfo ruleset, Action<SectionGimmickSettings> configure)
        {
            var beatmap = new MosuBeatmap
            {
                BeatmapInfo = new BeatmapInfo
                {
                    DifficultyName = "test",
                    Ruleset = ruleset,
                    Metadata = new BeatmapMetadata { Artist = "test", Title = "section gimmicks autoplay" },
                },
                Gimmicks = new MosuGimmickData
                {
                    Sections = new BeatmapSectionGimmicks
                    {
                        Sections =
                        {
                            new SectionGimmickSection
                            {
                                Id = 0,
                                StartTime = section_start,
                                EndTime = section_end,
                                Settings = new SectionGimmickSettings(),
                            }
                        }
                    },
                    Parsed = true,
                },
            };

            for (int i = 0; i < object_count; i++)
            {
                double time = start_time + i * object_spacing;
                float x = 100 + i * 22f;

                OsuHitObject obj;

                if (i % 3 == 2)
                {
                    obj = new Slider
                    {
                        StartTime = time,
                        Position = new Vector2(x, 192),
                        Path = new SliderPath(PathType.LINEAR, new[] { Vector2.Zero, new Vector2(80, 0) }),
                    };
                }
                else
                    obj = new HitCircle { StartTime = time, Position = new Vector2(x, 192) };

                obj.ApplyDefaults(new ControlPointInfo(), new BeatmapDifficulty());
                beatmap.HitObjects.Add(obj);
            }

            configure(beatmap.Gimmicks.Sections.Sections[0].Settings);
            return beatmap;
        }

        private void playAutoplay()
        {
            AddUntilStep("wait for player loaded", () => Player.IsLoaded && Player.Alpha == 1);
            AddUntilStep("gameplay clock running", () => Player.GameplayClockContainer.CurrentTime > 0);

            // Let the section be on screen before the runner screenshots.
            AddUntilStep("section on screen", () => Player.GameplayClockContainer.CurrentTime > section_start + (section_end - section_start) * 0.6);
        }

        [Test]
        public void TestAutoplaySectionHidden()
        {
            AddStep("load player", () => loadPlayer(r => buildMap(r, s => s.ForceHidden = true)));
            playAutoplay();
        }

        [Test]
        public void TestAutoplaySectionNoApproachCircle()
        {
            AddStep("load player", () => loadPlayer(r => buildMap(r, s => s.ForceNoApproachCircle = true)));
            playAutoplay();
        }

        [Test]
        public void TestAutoplaySectionHardRock()
        {
            AddStep("load player", () => loadPlayer(r => buildMap(r, s => s.ForceHardRock = true)));
            playAutoplay();
        }

        [Test]
        public void TestAutoplaySectionTraceable()
        {
            AddStep("load player", () => loadPlayer(r => buildMap(r, s => s.ForceTraceable = true)));
            playAutoplay();
        }

        [Test]
        public void TestAutoplaySectionCS()
        {
            AddStep("load player", () => loadPlayer(r => buildMap(r, s =>
            {
                s.EnableDifficultyOverrides = true;
                s.SectionCircleSize = 6f;
            })));
            playAutoplay();
        }

        [Test]
        public void TestAutoplaySectionAR()
        {
            AddStep("load player", () => loadPlayer(r => buildMap(r, s =>
            {
                s.EnableDifficultyOverrides = true;
                s.SectionApproachRate = 9.8f;
            })));
            playAutoplay();
        }

        [Test]
        public void TestAutoplaySectionWiggle()
        {
            AddStep("load player", () => loadPlayer(r => buildMap(r, s =>
            {
                s.ForceWiggle = true;
                s.WiggleStrength = 1f;
            })));
            playAutoplay();
        }

        [Test]
        public void TestAutoplaySectionFlashlight()
        {
            AddStep("load player", () => loadPlayer(r => buildMap(r, s => s.ForceFlashlight = true)));
            playAutoplay();
        }

        [Test]
        public void TestAutoplaySectionSingleTap()
        {
            AddStep("load player", () => loadPlayer(r => buildMap(r, s => s.ForceSingleTap = true)));
            playAutoplay();
        }

        [Test]
        public void TestAutoplaySectionAlternate()
        {
            AddStep("load player", () => loadPlayer(r => buildMap(r, s => s.ForceAlternate = true)));
            playAutoplay();
        }
    }
}
