// Blueprint container wiring the mosu selection handler (fake-clone -> source resolution).

using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Edit;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Edit.Compose.Components;

namespace osu.Game.Rulesets.MOsu.Delta.Edit
{
    public partial class DeltaBlueprintContainer : OsuBlueprintContainer
    {
        public DeltaBlueprintContainer(OsuHitObjectComposer composer)
            : base(composer)
        {
        }

        protected override SelectionHandler<HitObject> CreateSelectionHandler() => new DeltaSelectionHandler();
    }
}
