using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Platform;
using osu.Game.Rulesets.MOsu.Mods;
using osu.Game.Tests.Visual;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Tests
{
    public partial class TestSceneExpoJumpsTooltip : OsuManualInputManagerTestScene
    {
        [Resolved]
        private GameHost gameHost { get; set; } = null!;

        private void capture(string name)
        {
            ScreenshotHelper.Capture(gameHost, $"ExpoJumpsTooltip_{name}");
        }

        [Test]
        public void TestTooltipRenders()
        {
            ExpoJumpsTooltip tooltip = null!;

            AddStep("create tooltip", () =>
            {
                tooltip = new ExpoJumpsTooltip
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                };
                Child = tooltip;
            });

            AddUntilStep("tooltip loaded", () => tooltip.IsLoaded);
            AddAssert("tooltip has non-zero draw size", () => tooltip.DrawSize.X > 0 && tooltip.DrawSize.Y > 0);

            AddStep("set even", () => tooltip.SetState(true));
            AddWaitStep("wait", 2);
            AddStep("screenshot even", () => capture("EvenSpacing"));

            AddStep("set uneven", () => tooltip.SetState(false));
            AddWaitStep("wait", 2);
            AddStep("screenshot uneven", () => capture("UnevenSpacing"));
        }

        [Test]
        public void TestSettingCheckbox()
        {
            ExpoJumpsSetting setting = null!;

            AddStep("create setting", () =>
            {
                setting = new ExpoJumpsSetting
                {
                    Anchor = Anchor.Centre,
                    Origin = Anchor.Centre,
                };
                Child = setting;
            });

            AddUntilStep("setting loaded", () => setting.IsLoaded);
            AddAssert("setting implements IHasCustomTooltip", () => setting is IHasCustomTooltip);
            AddAssert("tooltip content is not null", () => ((IHasCustomTooltip)setting).TooltipContent != null);

            AddStep("screenshot off", () => capture("SettingOff"));

            AddStep("toggle on", () => setting.Current.Value = true);
            AddWaitStep("wait", 2);
            AddAssert("value is true", () => setting.Current.Value);
            AddStep("screenshot on", () => capture("SettingOn"));
        }

        [Test]
        public void TestTooltipWithHover()
        {
            ExpoJumpsSetting setting = null!;

            AddStep("create in tooltip container", () =>
            {
                Child = new TooltipContainer
                {
                    RelativeSizeAxes = Axes.Both,
                    Child = new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Child = setting = new ExpoJumpsSetting
                        {
                            Anchor = Anchor.Centre,
                            Origin = Anchor.Centre,
                        }
                    }
                };
            });

            AddUntilStep("setting loaded", () => setting.IsLoaded);

            AddStep("hover", () => InputManager.MoveMouseTo(setting));
            AddWaitStep("wait for tooltip", 5);
            AddStep("screenshot hover off", () => capture("HoverOff"));

            AddStep("toggle on", () => setting.Current.Value = true);
            AddWaitStep("wait", 2);
            AddStep("screenshot hover on", () => capture("HoverOn"));

            AddStep("unhover", () => InputManager.MoveMouseTo(new Vector2(0, -200)));
            AddWaitStep("wait", 2);
            AddStep("screenshot unhover", () => capture("Unhover"));
        }
    }
}
