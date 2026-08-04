using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Extensions;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Rulesets.MOsu.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Rulesets.UI;
using osu.Game.Tests.Visual;

namespace osu.Game.Rulesets.MOsu.Tests
{
    public partial class TestSceneAutoplayRandomV2 : TestSceneOsuPlayer
    {
        protected override bool HasCustomSteps => true;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private GameHost gameHost { get; set; } = null!;

        private const string beatmap_filename = "2462926 Armin van Buuren - In And Out Of Love.osz";

        private Live<BeatmapSetInfo>? importedSet;
        private OsuModRandomV2 randomMod = new OsuModRandomV2();

        [Resolved]
        private OsuConfigManager config { get; set; } = null!;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            AddSliderStep("ui scale", 0.8f, 1.6f, 1f, scale => config.SetValue(OsuSetting.UIScale, scale));
            AddSliderStep("aim distance multiplier", 1f, 10f, 1f, v =>
            {
                Logger.Log($"[TEST] aim distance slider -> {v}");
                randomMod.AimDistanceMultiplier.Value = v;
            });
            AddSliderStep("stream distance multiplier", 1f, 10f, 1f, v =>
            {
                Logger.Log($"[TEST] stream distance slider -> {v}");
                randomMod.StreamDistanceMultiplier.Value = v;
            });
            AddSliderStep("angle sharpness", 1f, 10f, 7f, v =>
            {
                Logger.Log($"[TEST] angle sharpness slider -> {v}");
                randomMod.AngleSharpness.Value = v;
            });
        }

        [Test]
        public void TestAutoplayWithRandomV2()
        {
            AddStep("import beatmap", () =>
            {
                var fullpath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", beatmap_filename);
                if (!File.Exists(fullpath))
                    Assert.Fail($"Beatmap file not found at {fullpath}");
                using var stream = File.OpenRead(fullpath);
                var importTask = new ImportTask(stream, beatmap_filename);
                importedSet = beatmapManager.Import(importTask).GetResultSafely();
                if (importedSet == null)
                    Assert.Fail("Failed to import beatmap");
            });

            AddStep("load player with mods", () =>
            {
                var osuRuleset = new MosuRuleset();
                var beatmapInfo = importedSet!.PerformRead(s => s.Beatmaps.First(b => b.Ruleset.ShortName == "osu"));
                var working = beatmapManager.GetWorkingBeatmap(beatmapInfo);

                Ruleset.Value = osuRuleset.RulesetInfo;
                Beatmap.Value = working;
                SelectedMods.Value = new Mod[] { randomMod, osuRuleset.GetAutoplayMod() };

                Player = CreatePlayer(osuRuleset);
                LoadScreen(Player);
            });

            AddUntilStep("wait for player loaded", () => Player.IsLoaded && Player.Alpha == 1);
            AddUntilStep("wait for track running", () => Beatmap.Value.Track.IsRunning);

            AddStep("bind live update", () =>
            {
                var drawableRuleset = (DrawableRuleset<OsuHitObject>)Player.DrawableRuleset;
                var drawableBeatmap = drawableRuleset.Beatmap;
                Logger.Log($"[TEST] drawableBeatmap type={drawableBeatmap.GetType().Name}");

                var replayFunc = () => drawableRuleset.ReplayScore?.Replay;

                var updateBeatmap = () =>
                {
                    randomMod.ApplyToBeatmap(drawableBeatmap);

                    var replay = replayFunc();
                    if (replay == null)
                    {
                        Logger.Log($"[TEST] replay is null");
                        return;
                    }

                    var autoplay = SelectedMods.Value.OfType<ModAutoplay>().FirstOrDefault();
                    if (autoplay == null)
                    {
                        Logger.Log($"[TEST] no autoplay mod found");
                        return;
                    }

                    var newReplay = autoplay.CreateReplayData(drawableBeatmap, SelectedMods.Value).Replay;
                    replay.Frames = newReplay.Frames;
                    Logger.Log($"[TEST] updated replay frames to {replay.Frames.Count}");
                };

                randomMod.AimDistanceMultiplier.ValueChanged += e =>
                {
                    Logger.Log($"[TEST] aim distance changed to {e.NewValue}");
                    updateBeatmap();
                };
                randomMod.StreamDistanceMultiplier.ValueChanged += e =>
                {
                    Logger.Log($"[TEST] stream distance changed to {e.NewValue}");
                    updateBeatmap();
                };
                randomMod.AngleSharpness.ValueChanged += e =>
                {
                    Logger.Log($"[TEST] angle sharpness changed to {e.NewValue}");
                    updateBeatmap();
                };
            });
        }
    }
}
