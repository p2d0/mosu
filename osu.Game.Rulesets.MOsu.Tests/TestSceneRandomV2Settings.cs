using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Testing;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.MOsu.Mods;
using osu.Game.Rulesets.MOsu.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Beatmaps;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Overlays.Settings;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Tests
{
    public partial class TestSceneRandomV2Settings : TestSceneMOsuBase
    {
        private RandomV2Settings settingsPanel = null!;
        private OsuModRandomV2 mod = null!;
        private OsuBeatmap beatmap = null!;

        [Test]
        public void TestRandomV2SettingsPanelLoads()
        {
            createModAndBeatmap();

            AddStep("create and add settings panel", () =>
            {
                settingsPanel = new RandomV2Settings(mod, beatmap, Array.Empty<Mod>(), () => null, new Bindable<IReadOnlyList<Mod>>(Array.Empty<Mod>()))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                };
                Add(settingsPanel);
            });

            AddUntilStep("panel has sliders", () =>
                settingsPanel.ChildrenOfType<SettingsSlider<float>>().Any()
                || settingsPanel.ChildrenOfType<SettingsSlider<int>>().Any());

            AddUntilStep("panel has checkboxes", () =>
                settingsPanel.ChildrenOfType<SettingsCheckbox>().Any());

            CaptureScreenshot("PanelLoads");
        }

        [Test]
        public void TestRandomV2SettingsLiveReprocess()
        {
            createModAndBeatmap();

            Vector2[] originalPositions = null!;

            AddStep("create and add settings panel", () =>
            {
                settingsPanel = new RandomV2Settings(mod, beatmap, Array.Empty<Mod>(), () => null, new Bindable<IReadOnlyList<Mod>>(Array.Empty<Mod>()))
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                };
                Add(settingsPanel);
                originalPositions = beatmap.HitObjects.OfType<OsuHitObject>().Select(h => h.Position).ToArray();
            });

            AddStep("change aim distance multiplier", () =>
                mod.AimDistanceMultiplier.Value = 20f);

            AddUntilStep("positions changed", () =>
            {
                var newPositions = beatmap.HitObjects.OfType<OsuHitObject>().Select(h => h.Position).ToArray();
                return newPositions.Where((p, i) => p != originalPositions[i]).Any();
            });

            CaptureScreenshot("LiveReprocess");
        }

        private void createModAndBeatmap()
        {
            AddStep("create mod and beatmap", () =>
            {
                mod = new OsuModRandomV2
                {
                    AimDistanceMultiplier = { Value = 10f },
                    Seed = { Value = 42 }
                };

                var controlPointInfo = new ControlPointInfo();
                controlPointInfo.Add(0, new TimingControlPoint { Time = 0, BeatLength = 500 });

                beatmap = new OsuBeatmap
                {
                    ControlPointInfo = controlPointInfo,
                    StackLeniency = 0
                };

                for (int i = 0; i < 20; i++)
                {
                    beatmap.HitObjects.Add(new HitCircle
                    {
                        StartTime = 1000 + i * 200,
                        Position = new Vector2(200 + (i % 4) * 50, 200 + (i / 4) * 50),
                        NewCombo = i % 5 == 0
                    });
                }

                mod.ApplyToBeatmap(beatmap);
            });
        }
    }
}
