// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Platform;
using osu.Game.Tests.Visual;
using osuTK.Graphics;

namespace osu.Game.Rulesets.MOsu.Tests
{
    public partial class TestSceneOsuGame : OsuTestScene
    {
        protected override bool UseFreshStoragePerRun => true;

        [BackgroundDependencyLoader]
        private void load()
        {
            Children = new Drawable[]
            {
                new Box
                {
                    RelativeSizeAxes = Axes.Both,
                    Colour = Color4.Black,
                },
            };

            AddGame(new OsuGame());
        }

        [Resolved]
        private GameHost gameHost { get; set; } = null!;

        [Test]
        public void TestOsuGame()
        {
            AddWaitStep("wait for game to load", 3);
            AddStep("screenshot", () => ScreenshotHelper.Capture(gameHost, "OsuGame_OsuGame"));
            AddWaitStep("wait for screenshot", 1);
        }
    }
}
