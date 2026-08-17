using System;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.MOsu.Delta.Beatmaps;
using osu.Game.Rulesets.MOsu.Delta.Gimmicks;
using osu.Game.Rulesets.MOsu.UI;
using osu.Game.Rulesets.MOsu.Delta.UI;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Tests.Delta.Gimmicks
{
    /// <summary>
    /// Repro: a bounded section with ForceWiggle must only wiggle objects inside the section.
    /// Drawables are pooled and reused; the guarded hooks resolve the section per object, so a
    /// drawable reused for an out-of-section object must not leak the wiggle.
    /// </summary>
    public partial class TestSceneSectionFunMods : TestSceneMOsuBase
    {
        private SectionGimmickFunModsOverlay overlay = null!;
        private DrawableHitCircle drawable = null!;

        [Test]
        public void TestWiggleScopedToSectionPerObject()
        {
            AddStep("build overlay + drawable", () =>
            {
                var beatmap = new DeltaBeatmap
                {
                    Gimmicks = new DeltaGimmickData
                    {
                        Sections = new BeatmapSectionGimmicks
                        {
                            Sections =
                            {
                                new SectionGimmickSection
                                {
                                    Id = 0,
                                    StartTime = 1000,
                                    EndTime = 2000,
                                    Settings = new SectionGimmickSettings { ForceWiggle = true, WiggleStrength = 1f },
                                }
                            }
                        }
                    },
                };

                beatmap.HitObjects.Add(new HitCircle { StartTime = 1500, Position = new Vector2(256, 192) });

                overlay = new SectionGimmickFunModsOverlay(beatmap, null!, Array.Empty<Mod>());
                drawable = new DrawableHitCircle(createCircle(1500));
                Add(drawable);
            });

            AddUntilStep("drawable loaded", () => drawable.IsLoaded);

            AddStep("apply guarded wiggle hook once", () =>
                overlay.applySectionScopedVisibilityMod(new OsuModWiggle(), drawable, s => s.ForceWiggle,
                    (m, s) => m.Strength.Value = Math.Clamp(s.WiggleStrength, 0.1f, 2f)));

            AddAssert("in-section object wiggles", () =>
            {
                drawable.Apply(createCircle(1500));

                // The drawable holds an in-section object; firing a state change must schedule wiggle moves.
                drawable.RefreshStateTransforms();
                bool wiggled = drawable.Transforms.Any(t => t.TargetMember == "Position");
                Logger.Log($"[TEST] wiggle in-section object: {wiggled}");
                return wiggled;
            });

            AddAssert("out-of-section object does not wiggle", () =>
            {
                // Pool reuse: the same drawable is now applied to an object outside the section.
                drawable.Apply(createCircle(300));
                drawable.ClearTransforms();
                drawable.RefreshStateTransforms();

                bool wiggled = drawable.Transforms.Any(t => t.TargetMember == "Position");
                Logger.Log($"[TEST] wiggle out-of-section object: {wiggled}");
                return !wiggled;
            });

            AddStep("remove bare drawable", () => Remove(drawable, false));
        }

        private static HitCircle createCircle(double startTime)
        {
            var circle = new HitCircle { StartTime = startTime, Position = new Vector2(256, 192) };
            circle.ApplyDefaults(new osu.Game.Beatmaps.ControlPoints.ControlPointInfo(), new osu.Game.Beatmaps.BeatmapDifficulty());
            return circle;
        }
    }
}
