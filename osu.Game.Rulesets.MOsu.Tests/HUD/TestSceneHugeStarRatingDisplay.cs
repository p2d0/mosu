using NUnit.Framework;
using osu.Framework.Graphics;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osuTK;


namespace osu.Game.Rulesets.MOsu.Tests.HUD
{
    public partial class TestSceneHugeStarRatingDisplay : TestSceneMOsuBase
    {
        private const float target_width = 600f;

        [Test]
        public void TestHugeStarRatingDisplay()
        {
            StarRatingDisplay starRating = null!;

            AddStep("add star rating display (3.88 stars)", () =>
            {
                starRating = new StarRatingDisplay(new StarDifficulty(8.92, 0))
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                };

                Child = starRating;
            });

            AddUntilStep("wait for display sized", () => starRating.DrawWidth > 0);

            AddStep("scale proportionally to at least 600px wide", () =>
            {
                float scale = target_width / (starRating.DrawWidth * starRating.Scale.X);
                starRating.Scale = new Vector2(scale);
            });

            AddAssert("display is at least 600px wide on screen", () => starRating.DrawWidth * starRating.Scale.X >= target_width - 1);
            AddAssert("scale is uniform (proportional)", () => starRating.Scale.X == starRating.Scale.Y);
        }
    }
}
