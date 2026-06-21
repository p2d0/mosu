using System;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Rulesets.MOsu.Database;
using osu.Game.Rulesets.MOsu.UI.LocalUser.Sections.Ranks;
using osu.Game.Rulesets.Osu;
using osu.Game.Scoring;
using osu.Game.Tests.Visual;
using osu.Game.Users;
using osu.Game.Models;

namespace osu.Game.Rulesets.MOsu.Tests
{
    [TestFixture]
    public partial class TestSceneDrawableProfileLocalScore : OsuTestScene
    {
        protected override bool UseFreshStoragePerRun => true;

        [Resolved]
        private GameHost gameHost { get; set; } = null!;

        [TearDown]
        public void TearDownScreenshot() => ScreenshotHelper.Capture(gameHost);

        private OsuRuleset ruleset = null!;
        private LocalUserManager localUserManager = null!;
        private RealmAccess realm = null!;
        private MOsuRealmAccess mosuRealm = null!;

        [BackgroundDependencyLoader]
        private void load(RealmAccess r, IAPIProvider api)
        {
            realm = r;
            ruleset = new OsuRuleset();
            mosuRealm = new MOsuRealmAccess(LocalStorage);
            Dependencies.Cache(mosuRealm);
            Dependencies.Cache(localUserManager = new LocalUserManager(ruleset, r, mosuRealm, api));
            Dependencies.Cache(new OverlayColourProvider(OverlayColourScheme.Green));
        }

        [Test]
        public void TestScoreWithNullBeatmapDoesNotCrash()
        {
            AddStep("create score with null beatmap", () =>
            {
                var rs = ruleset.RulesetInfo;
                var user = new RealmUser { Username = "TestUser", OnlineID = 1 };

                var score = new ScoreInfo(null, rs, user)
                {
                    TotalScore = 1000000,
                    MaxCombo = 50,
                    Accuracy = 0.95,
                    PP = 150.5,
                    Rank = ScoreRank.S,
                    Date = DateTimeOffset.Now,
                    DeletePending = false,
                    Mods = Array.Empty<osu.Game.Rulesets.Mods.Mod>(),
                };

                // Simulate a score whose beatmap was deleted/updated (BeatmapInfo cleared)
                score.BeatmapInfo = null;

                var drawable = new DrawableProfileLocalScore(score, ruleset);

                Child = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = drawable,
                };
            });
            AddUntilStep("wait for drawable to load", () => Child?.Children.FirstOrDefault()?.LoadState == LoadState.Ready);
        }
    }
}
