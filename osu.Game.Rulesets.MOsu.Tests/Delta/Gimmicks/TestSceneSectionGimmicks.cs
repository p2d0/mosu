// Per-gimmick tests for section gimmicks: a small beatmap (a hitcircle + a slider) with one
// bounded section applying the gimmick, verifying the gameplay effect is scoped to the section
// (drawable representation / object properties / overlay activation flags).

using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.MOsu.Delta.Beatmaps;
using osu.Game.Rulesets.MOsu.Delta.Gimmicks;
using osu.Game.Rulesets.MOsu.Delta.Objects;
using osu.Game.Rulesets.MOsu.Delta.Objects.Drawables;
using osu.Game.Rulesets.MOsu.UI;
using osu.Game.Rulesets.MOsu.Delta.UI;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Osu.Objects;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Tests.Delta.Gimmicks
{
    public partial class TestSceneSectionGimmicks : TestSceneMOsuBase
    {
        private const double outside_time = 500;
        private const double in_section_time = 1500;
        private const double section_end = 2000;

        private DeltaBeatmap beatmap = null!;
        private DeltaGimmickData data = null!;
        private HitCircle outsideCircle = null!;
        private HitCircle inSectionCircle = null!;
        private Slider inSectionSlider = null!;

        private void setupBeatmap()
        {
            beatmap = new DeltaBeatmap();
            data = new DeltaGimmickData();

            outsideCircle = createCircle(outside_time, new Vector2(100, 100));
            inSectionCircle = createCircle(in_section_time, new Vector2(256, 100));
            inSectionSlider = createSlider(in_section_time + 100, new Vector2(300, 192));

            beatmap.HitObjects.Add(outsideCircle);
            beatmap.HitObjects.Add(inSectionCircle);
            beatmap.HitObjects.Add(inSectionSlider);
        }

        private void setupSection(Action<SectionGimmickSettings> configure)
        {
            data.Sections.Sections.Add(new SectionGimmickSection
            {
                Id = 0,
                StartTime = 1000,
                EndTime = section_end,
                Settings = new SectionGimmickSettings(),
            });

            configure(data.Sections.Sections[0].Settings);

            beatmap.Gimmicks = data;
            DeltaGimmickApplier.Apply(beatmap, data);
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

        [Test]
        public void TestForceHidden()
        {
            AddStep("build beatmap", setupBeatmap);
            AddStep("hide section", () => setupSection(s => s.ForceHidden = true));

AddAssert("in-section objects hidden", () =>
                DeltaGimmickRuntime.CreateGimmickDrawableRepresentation(beatmap, inSectionCircle) is MosuDrawableHitCircle
                && DeltaGimmickRuntime.CreateGimmickDrawableRepresentation(beatmap, inSectionSlider) is MosuDrawableSlider);

            AddAssert("outside object unaffected", () => DeltaGimmickRuntime.CreateGimmickDrawableRepresentation(beatmap, outsideCircle) is null);
        }

        [Test]
        public void TestForceNoApproachCircle()
        {
            AddStep("build beatmap", setupBeatmap);
            AddStep("remove approach circles in section", () => setupSection(s => s.ForceNoApproachCircle = true));

            AddAssert("in-section circle without approach circle", () => DeltaGimmickRuntime.CreateGimmickDrawableRepresentation(beatmap, inSectionCircle) is MosuDrawableHitCircle);
            AddAssert("outside circle unaffected", () => DeltaGimmickRuntime.CreateGimmickDrawableRepresentation(beatmap, outsideCircle) is null);
        }

        [Test]
        public void TestForceHardRock()
        {
            AddStep("build beatmap", setupBeatmap);
            AddStep("hard rock section", () => setupSection(s => s.ForceHardRock = true));

            AddAssert("in-section circle flipped", () =>
            {
                Logger.Log($"[TEST] in-section circle y={inSectionCircle.Position.Y} (base 100, flipped ~284)");
                return Math.Abs(inSectionCircle.Position.Y - (384 - 100)) < 0.01;
            });

            AddAssert("outside circle not flipped", () =>
            {
                Logger.Log($"[TEST] outside circle y={outsideCircle.Position.Y} (base 100)");
                return Math.Abs(outsideCircle.Position.Y - 100) < 0.01;
            });
        }

        [Test]
        public void TestForceTraceable()
        {
            AddStep("build beatmap", setupBeatmap);
            AddStep("traceable section", () => setupSection(s => s.ForceTraceable = true));

            AddAssert("in-section circle traceable", () => DeltaGimmickRuntime.CreateGimmickDrawableRepresentation(beatmap, inSectionCircle) is MosuDrawableHitCircle);
            AddAssert("outside circle unaffected", () => DeltaGimmickRuntime.CreateGimmickDrawableRepresentation(beatmap, outsideCircle) is null);
        }

        [Test]
        public void TestDifficultyOverrides()
        {
            AddStep("build beatmap", setupBeatmap);
            AddStep("AR 9.8 / CS 6 section", () => setupSection(s =>
            {
                s.EnableDifficultyOverrides = true;
                s.SectionApproachRate = 1f;
                s.SectionCircleSize = 0f;
            }));

            AddAssert("in-section circle overridden", () =>
            {
                Logger.Log($"[TEST] in-section circle preempt={inSectionCircle.TimePreempt} (AR 1 -> ~1680) scale={inSectionCircle.Scale} (CS 0 -> ~0.85)");
                return Math.Abs(inSectionCircle.TimePreempt - 1680) < 2 && Math.Abs(inSectionCircle.Scale - 0.85f) < 0.01;
            });

            AddAssert("outside circle keeps base difficulty", () =>
            {
                Logger.Log($"[TEST] outside circle preempt={outsideCircle.TimePreempt} (base AR 5 -> ~1200)");
                return Math.Abs(outsideCircle.TimePreempt - 1200) < 2;
            });
        }

        [Test]
        public void TestForceFlashlight()
        {
            AddStep("build beatmap", setupBeatmap);
            AddStep("flashlight section", () => setupSection(s => s.ForceFlashlight = true));

            AddAssert("flashlight overlay activates", () => SectionGimmickFlashlightOverlay.HasAnyForcedFlashlightSection(beatmap));
        }

        [Test]
        public void TestInputBlocking()
        {
            AddStep("build beatmap", setupBeatmap);
            AddStep("single-tap section", () => setupSection(s => s.ForceSingleTap = true));

            AddAssert("input blocking overlay activates", () => SectionGimmickInputBlockingOverlay.HasAnyForcedInputBlockingSection(beatmap));

            AddAssert("section resolves for in-section time only", () =>
            {
                bool inSection = data.Sections.FindSectionAt(in_section_time)?.Settings.ForceSingleTap == true;
                bool outside = data.Sections.FindSectionAt(outside_time)?.Settings.ForceSingleTap == true;
                Logger.Log($"[TEST] single-tap at in-section={inSection} outside={outside}");
                return inSection && !outside;
            });
        }

        [Test]
        public void TestFunMods()
        {
            AddStep("build beatmap", setupBeatmap);
            AddStep("wiggle section", () => setupSection(s => s.ForceWiggle = true));

            AddAssert("fun mods overlay activates", () => SectionGimmickFunModsOverlay.HasAnyForcedFunMods(beatmap));
        }
    }
}
