using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.MOsu.Database;
using osu.Game.Rulesets.MOsu.UI;
using osu.Game.Rulesets.Osu;
using osu.Game.Rulesets.Osu.Mods;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Tests
{
    [TestFixture]
    public partial class TestSceneBeatmapModPresetWedge : OsuManualInputManagerTestScene
    {
        private BeatmapModPresetWedge wedge = null!;
        private GameHost gameHost = null!;
        protected MOsuRealmAccess MOsuRealm { get; set; } = null!;
        protected override bool UseFreshStoragePerRun => true;

        [Resolved]
        private Bindable<RulesetInfo> ruleset { get; set; } = null!;

        [Resolved]
        private BeatmapDifficultyCache difficultyCache { get; set; } = null!;

        [Cached]
        private readonly Bindable<IReadOnlyList<Mod>> selectedMods = new Bindable<IReadOnlyList<Mod>>(Array.Empty<Mod>());

        [BackgroundDependencyLoader]
        private void load(GameHost host)
        {
            gameHost = host;
            Dependencies.Cache(MOsuRealm = new MOsuRealmAccess(LocalStorage, "mosurealm-test", host.UpdateThread));
            Dependencies.Cache(new OverlayColourProvider(OverlayColourScheme.Green));
            Dependencies.CacheAs<INotificationOverlay>(new StubNotificationOverlay());
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            Add(wedge = new BeatmapModPresetWedge
            {
                Size = new Vector2(400, 400),
                RelativeSizeAxes = Axes.None,
                Anchor = Anchor.Centre,
                Origin = Anchor.Centre,
                State = { Value = Visibility.Visible }
            });
        }

        [SetUp]
        public void SetUp()
        {
            MOsuRealm.Write(r => r.RemoveAll<BeatmapModPreset>());
        }

        [Test]
        public void TestPresetDisplay()
        {
            string beatmapHash = Guid.NewGuid().ToString();

            AddStep("create beatmap with presets", () =>
            {
                var working = CreateWorkingBeatmap(new OsuRuleset().RulesetInfo);
                working.BeatmapInfo.MD5Hash = beatmapHash;
                if (working.Beatmap != null)
                    working.Beatmap.BeatmapInfo.MD5Hash = beatmapHash;

                MOsuRealm.Write(r =>
                {
                    r.Add(new BeatmapModPreset
                    {
                        Name = "KEKEKEKEK",
                        BeatmapMD5Hash = beatmapHash,
                        Ruleset = r.Find<RulesetInfo>(ruleset.Value.ShortName) ?? ruleset.Value,
                        Mods = new Mod[] { new OsuModDoubleTime() { SpeedChange = { Value = 2.3f } }, new OsuModHidden() { OnlyFadeApproachCircles = { Value = true } } }
                    });

                    r.Add(new BeatmapModPreset
                    {
                        Name = "KEKEKEKEK",
                        BeatmapMD5Hash = beatmapHash,
                        Ruleset = r.Find<RulesetInfo>(ruleset.Value.ShortName) ?? ruleset.Value,
                        Mods = new Mod[] { new OsuModHidden() }
                    });
                });

                Beatmap.Value = working;
            });

            AddStep("wait for presets to render", () => System.Threading.Thread.Sleep(500));
            AddStep("screenshot", () => ScreenshotHelper.Capture(gameHost, "BeatmapModPresetWedge_PresetDisplay"));
            AddWaitStep("wait for screenshot", 1);
        }

        [Test]
        public void TestShowHide()
        {
            AddStep("hide wedge", () => wedge.Hide());
            AddStep("show wedge", () => wedge.Show());
            AddStep("screenshot", () => ScreenshotHelper.Capture(gameHost, "BeatmapModPresetWedge_ShowHide"));
            AddWaitStep("wait for screenshot", 1);
        }
    }
}
