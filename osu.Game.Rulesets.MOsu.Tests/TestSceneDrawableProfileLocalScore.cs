using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Overlays;
using osu.Game.Rulesets.MOsu.UI.LocalUser.Sections.Ranks;
using osu.Game.Models;
using osu.Game.Scoring;
using osu.Game.Users;

namespace osu.Game.Rulesets.MOsu.Tests
{
    public partial class TestSceneDrawableProfileLocalScore : TestSceneMOsuBase
    {
        [BackgroundDependencyLoader]
        private void loadOverlay() => Dependencies.Cache(new OverlayColourProvider(OverlayColourScheme.Green));

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
                    Statistics = new Dictionary<osu.Game.Rulesets.Scoring.HitResult, int>
                    {
                        { osu.Game.Rulesets.Scoring.HitResult.Great, 45 },
                        { osu.Game.Rulesets.Scoring.HitResult.Good, 3 },
                        { osu.Game.Rulesets.Scoring.HitResult.Ok, 2 },
                        { osu.Game.Rulesets.Scoring.HitResult.Miss, 1 },
                    },
                };

                score.BeatmapInfo = null;

                var drawable = new DrawableProfileLocalScore(score, ruleset);

                Child = new Container
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = drawable,
                };
            });
            AddWaitStep("wait for drawable to load", 2);
        }
    }
}
