// Visual autoplay tests for section gimmicks — one gimmick per test case, 15 objects
// (10 hitcircles + 5 sliders interleaved) with one bounded section covering the middle of the
// map. Each test plays its map with autoplay until the section is on screen, then ends so the
// runner screenshots the result. No assertions — verification is visual.
//
// Maps are built as inline .osu text and decoded (see TestSceneHitObjectsGimmicksAutoplay).
//
// PlayerTestScene-derived scenes are excluded from the automated --auto runner; run manually
// (test browser, or --filter without --auto).

using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using osu.Framework.Audio;
using osu.Game.IO;
using osu.Framework.Timing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Formats;
using osu.Game.Models;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.MOsu.Tests.Screens;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.MOsu.Tests.Delta.Gimmicks
{
    public partial class TestSceneSectionGimmicksAutoplay : TestSceneOsuPlayer
    {
        protected override bool HasCustomSteps => true;

        private const int object_count = 15;
        private const double start_time = 1000;
        private const double object_spacing = 500;
        private const double section_start = 2500;
        private const double section_end = 5500;

        private void loadPlayer(Func<string> buildText)
        {
            var osuRuleset = new MosuRuleset();
            Ruleset.Value = osuRuleset.RulesetInfo;
            Beatmap.Value = createWorkingBeatmap(buildText());
            SelectedMods.Value = new Mod[] { osuRuleset.GetAutoplayMod()! };
            Player = CreatePlayer(osuRuleset)!;
            LoadScreen(Player);
        }

        /// <summary>
        /// Builds a 15-object map as .osu text with one bounded section covering the middle
        /// (2500-5500), applying one section gimmick.
        /// </summary>
        private static string buildMapText(string gimmickKeyValues)
        {
            var sb = new StringBuilder();
            sb.AppendLine("osu file format v14");
            sb.AppendLine();
            sb.AppendLine("[General]");
            sb.AppendLine("AudioFilename: audio.mp3");
            sb.AppendLine("Mode: 0");
            sb.AppendLine();
            sb.AppendLine("[Metadata]");
            sb.AppendLine("Title:section gimmicks autoplay");
            sb.AppendLine("Artist:test");
            sb.AppendLine("Creator:test");
            sb.AppendLine("Version:test");
            sb.AppendLine();
            sb.AppendLine("[Difficulty]");
            sb.AppendLine("HPDrainRate:5");
            sb.AppendLine("CircleSize:5");
            sb.AppendLine("OverallDifficulty:5");
            sb.AppendLine("ApproachRate:5");
            sb.AppendLine("SliderMultiplier:1");
            sb.AppendLine("SliderTickRate:1");
            sb.AppendLine();
            sb.AppendLine("[TimingPoints]");
            sb.AppendLine("0,500,4,2,0,100,1,0");
            sb.AppendLine();
            sb.AppendLine("[HitObjects]");

            for (int i = 0; i < object_count; i++)
            {
                double time = start_time + i * object_spacing;
                float x = 100 + i * 22f;

                if (i % 3 == 2)
                    sb.AppendLine(FormattableString.Invariant($"{x:0},192,{time:0},2,0,L|{x + 80:0}:192,1,80,0:0,0:0:0:0:"));
                else
                    sb.AppendLine(FormattableString.Invariant($"{x:0},192,{time:0},1,0,0:0:0:0:"));
            }

            sb.AppendLine();
            sb.AppendLine("[BeatmapSectionGimmicks]");
            sb.AppendLine(FormattableString.Invariant($"0,{section_start:0},{section_end:0},{gimmickKeyValues}"));

            return sb.ToString();
        }

        private WorkingBeatmap createWorkingBeatmap(string osuText)
        {
            using var reader = new LineBufferedReader(new MemoryStream(Encoding.UTF8.GetBytes(osuText)));
            var decoded = osu.Game.Beatmaps.Formats.Decoder.GetDecoder<Beatmap>(reader).Decode(reader);

            // Give the beatmap a resolvable file path so the gimmick runtime can serve the raw
            // text via GetStream (parsing is gated on BeatmapInfo.Path, which is file-derived).
            var realmFile = new RealmFile { Hash = "gimmick-test" };
            decoded.BeatmapInfo.BeatmapSet = new BeatmapSetInfo { Files = { new RealmNamedFileUsage(realmFile, "test.osu") } };
            decoded.BeatmapInfo.Hash = "gimmick-test";

            return new GimmickTextWorkingBeatmap(decoded, osuText, Clock, Audio);
        }

        private class GimmickTextWorkingBeatmap : ClockBackedTestWorkingBeatmap
        {
            private readonly string text;

            public GimmickTextWorkingBeatmap(IBeatmap beatmap, string text, IFrameBasedClock referenceClock, AudioManager audio)
                : base(beatmap, null, referenceClock, audio)
            {
                this.text = text;
            }

            public override Stream GetStream(string storagePath) => new MemoryStream(Encoding.UTF8.GetBytes(text));
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
            AddStep("load player", () => loadPlayer(() => buildMapText("ForceHidden=True")));
            playAutoplay();
        }

        [Test]
        public void TestAutoplaySectionNoApproachCircle()
        {
            AddStep("load player", () => loadPlayer(() => buildMapText("ForceNoApproachCircle=True")));
            playAutoplay();
        }

        [Test]
        public void TestAutoplaySectionHardRock()
        {
            AddStep("load player", () => loadPlayer(() => buildMapText("ForceHardRock=True")));
            playAutoplay();
        }

        [Test]
        public void TestAutoplaySectionTraceable()
        {
            AddStep("load player", () => loadPlayer(() => buildMapText("ForceTraceable=True")));
            playAutoplay();
        }

        [Test]
        public void TestAutoplaySectionCS()
        {
            AddStep("load player", () => loadPlayer(() => buildMapText("EnableDifficultyOverrides=True|SectionCircleSize=0")));
            playAutoplay();
        }

        [Test]
        public void TestAutoplaySectionAR()
        {
            AddStep("load player", () => loadPlayer(() => buildMapText("EnableDifficultyOverrides=True|SectionApproachRate=1")));
            playAutoplay();
        }

        [Test]
        public void TestAutoplaySectionWiggle()
        {
            AddStep("load player", () => loadPlayer(() => buildMapText("ForceWiggle=True|WiggleStrength=1")));
            playAutoplay();
        }

        [Test]
        public void TestAutoplaySectionFlashlight()
        {
            AddStep("load player", () => loadPlayer(() => buildMapText("ForceFlashlight=True")));
            playAutoplay();
        }

        [Test]
        public void TestAutoplaySectionSingleTap()
        {
            AddStep("load player", () => loadPlayer(() => buildMapText("ForceSingleTap=True")));
            playAutoplay();
        }

        [Test]
        public void TestAutoplaySectionAlternate()
        {
            AddStep("load player", () => loadPlayer(() => buildMapText("ForceAlternate=True")));
            playAutoplay();
        }
    }
}
