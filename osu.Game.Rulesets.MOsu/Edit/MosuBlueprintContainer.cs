// Blueprint container wiring the mosu selection handler (fake-clone -> source resolution).

using osu.Game.Rulesets.Edit;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Osu.Edit;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Screens.Edit.Compose.Components;

namespace osu.Game.Rulesets.MOsu.Edit
{
    public partial class MosuBlueprintContainer : OsuBlueprintContainer
    {
        public MosuBlueprintContainer(OsuHitObjectComposer composer)
            : base(composer)
        {
        }

        protected override SelectionHandler<HitObject> CreateSelectionHandler() => new MosuSelectionHandler();
    }
}
