
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Containers;
using osu.Framework.Testing;
using osu.Game.Graphics.Containers;
using osu.Game.Overlays;
using osu.Game.Overlays.Toolbar;

namespace osu.Game.Rulesets.MOsu.Extensions;

/// <summary>
/// Collect dirty logic to get target drawable from <see cref="OsuGame"/>
/// </summary>
public static class OsuGameExtensions
{
    private static Container? getBasePlacementContainer(this OsuGame game)
        => game.Children.OfType<Container>().FirstOrDefault(c => c.ChildrenOfType<WaveOverlayContainer>().Any());

    public static Container? GetWaveOverlayPlacementContainer(this OsuGame game)
    {
        // will place the container where components of an WaveOverlayContainer type exist
        return game.getBasePlacementContainer()?.Children.OfType<Container>().FirstOrDefault(c => c.Children.OfType<WaveOverlayContainer>().Any());
    }

    public static FillFlowContainer? GetToolbarContainer(this OsuGame game)
    {
        // will place the container where components of an WaveOverlayContainer type exist
        var grid = game.Toolbar.Children.OfType<GridContainer>().FirstOrDefault();

        // 2. Get the "Right buttons" container.
        // In the source code, Content is a 2D array. The right buttons are Row 0, Column 2.
        var rightButtonContainer = grid?.Content[0][2] as Container;

        // 3. Get the FillFlowContainer from inside that container
        var rightButtonsFlow = rightButtonContainer?.Children.OfType<FillFlowContainer>().FirstOrDefault();

        return rightButtonsFlow;
        // return game.getBasePlacementContainer()?.Children.OfType<Container>().FirstOrDefault(c => c.Children.OfType<WaveOverlayContainer>().Any());
    }

    public static SettingsOverlay? GetSettingsOverlay(this OsuGame game)
        => game.getBasePlacementContainer()?.ChildrenOfType<SettingsOverlay>().FirstOrDefault();
}
