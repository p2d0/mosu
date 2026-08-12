// Visual autoplay tests for hitobject gimmicks — one gimmick per test case, 15 objects
// (10 hitcircles + 5 sliders interleaved). Each test plays its map with autoplay until the
// gimmicked objects are on screen, then ends so the runner screenshots the result.
// No assertions — verification is visual.
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
    public partial class TestSceneHitObjectsGimmicksAutoplay : TestSceneOsuPlayer
    {
        protected override bool HasCustomSteps => true;

        private const int object_count = 15;
        private const double start_time = 1000;
        private const double object_spacing = 500;

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
        /// Builds 15 objects (10 hitcircles + 5 sliders interleaved, every 500ms), all with
        /// gimmick entries, then runs <paramref name="configure"/> over the entries.
        /// </summary>
        private MosuBeatmap buildMap(RulesetInfo ruleset, Action<System.Collections.Generic.List<HitObjectGimmickEntry>> configure)
        {
            var beatmap = new MosuBeatmap
            {
                BeatmapInfo = new BeatmapInfo
                {
                    DifficultyName = "test",
                    Ruleset = ruleset,
                    Metadata = new BeatmapMetadata { Artist = "test", Title = "hitobject gimmicks autoplay" },
                },
                Gimmicks = new MosuGimmickData
                {
                    HitObjectGimmicks = new BeatmapHitObjectGimmicks(),
                    Parsed = true,
                },
            };

            var entries = new System.Collections.Generic.List<HitObjectGimmickEntry>();

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

                entries.Add(new HitObjectGimmickEntry
                {
                    ObjectId = MosuGimmickApplier.GetObjectId(obj),
                    StartTime = obj.StartTime,
                    ComboIndexWithOffsets = obj.ComboIndexWithOffsets,
                    Settings = new HitObjectGimmickSettings(),
                });
            }

            beatmap.Gimmicks.HitObjectGimmicks.Entries.AddRange(entries);
            configure(entries);
            return beatmap;
        }

        /// <summary>
        /// Plays with autoplay until the map is on screen, then lets the runner screenshot
        /// (no assertions — verification is visual).
        /// </summary>
        private void playAutoplay()
        {
            AddUntilStep("wait for player loaded", () => Player.IsLoaded && Player.Alpha == 1);
            AddUntilStep("gameplay clock running", () => Player.GameplayClockContainer.CurrentTime > 0);

            AddUntilStep("map mid-way through", () => Player.GameplayClockContainer.CurrentTime > start_time + object_count * object_spacing * 0.5);
        }

        [Test]
        public void TestAutoplayFakeNote()
        {
            AddStep("load player", () => loadPlayer(r => buildMap(r, entries => entries.ForEach(e => e.Settings.IsFakeNote = true))));
            playAutoplay();
        }

        [Test]
        public void TestAutoplayHidden()
        {
            AddStep("load player", () => loadPlayer(r => buildMap(r, entries => entries.ForEach(e => e.Settings.ForceHidden = true))));
            playAutoplay();
        }

        [Test]
        public void TestAutoplayNoApproachCircle()
        {
            AddStep("load player", () => loadPlayer(r => buildMap(r, entries => entries.ForEach(e => e.Settings.ForceNoApproachCircle = true))));
            playAutoplay();
        }

        [Test]
        public void TestAutoplayHardRock()
        {
            AddStep("load player", () => loadPlayer(r => buildMap(r, entries => entries.ForEach(e => e.Settings.ForceHardRock = true))));
            playAutoplay();
        }

        [Test]
        public void TestAutoplayTraceable()
        {
            AddStep("load player", () => loadPlayer(r => buildMap(r, entries => entries.ForEach(e => e.Settings.ForceTraceable = true))));
            playAutoplay();
        }

        [Test]
        public void TestAutoplayCS()
        {
            AddStep("load player", () => loadPlayer(r => buildMap(r, entries => entries.ForEach(e =>
            {
                e.Settings.EnableDifficultyOverrides = true;
                e.Settings.SectionCircleSize = 6f;
            }))));
            playAutoplay();
        }

        [Test]
        public void TestAutoplayAR()
        {
            AddStep("load player", () => loadPlayer(r => buildMap(r, entries => entries.ForEach(e =>
            {
                e.Settings.EnableDifficultyOverrides = true;
                e.Settings.SectionApproachRate = 9.8f;
            }))));
            playAutoplay();
        }
    }
}
