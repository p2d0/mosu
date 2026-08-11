// MOsu's editor composer: uses the gimmick-aware editor ruleset for the compose
// playfield (toolbox groups are added in the load step).

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Osu.Edit;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.MOsu.Edit
{
    public partial class MosuHitObjectComposer : OsuHitObjectComposer
    {
        public MosuHitObjectComposer(Ruleset ruleset)
            : base(ruleset)
        {
        }

        protected override DrawableRuleset<OsuHitObject> CreateDrawableRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod> mods)
            => new MosuEditorDrawableRuleset(ruleset, beatmap, mods);
    }
}
