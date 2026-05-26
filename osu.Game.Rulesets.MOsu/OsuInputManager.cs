// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Input.Bindings;
using osu.Game.Database;
using osu.Game.Input.Bindings;
using osu.Game.Rulesets.Osu.Objects.Drawables;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.MOsu
{
    public partial class OsuInputManager : osu.Game.Rulesets.Osu.OsuInputManager
    {
        public OsuInputManager(RulesetInfo ruleset)
            : base(ruleset)
        {
        }

        protected override KeyBindingContainer<OsuAction> CreateKeyBindingContainer(RulesetInfo ruleset, int variant, SimultaneousBindingMode unique)
            => new OsuKeyBindingContainer(ruleset, variant, unique);

        public new bool AllowGameplayInputs
        {
            get => ((OsuKeyBindingContainer)KeyBindingContainer).AllowGameplayInputs;
            set => ((OsuKeyBindingContainer)KeyBindingContainer).AllowGameplayInputs = value;
        }

        private partial class OsuKeyBindingContainer : RulesetKeyBindingContainer
        {
            private bool allowGameplayInputs = true;

            [Resolved]
            private RealmAccess realm { get; set; } = null!;

            public bool AllowGameplayInputs
            {
                get => allowGameplayInputs;
                set
                {
                    allowGameplayInputs = value;
                    ReloadMappings();
                }
            }

            public OsuKeyBindingContainer(RulesetInfo ruleset, int variant, SimultaneousBindingMode unique)
                : base(ruleset, variant, unique)
            {
            }

            protected override void ReloadMappings(IQueryable<RealmKeyBinding> realmKeyBindings)
            {
                var osuBindings = realm.Realm.All<RealmKeyBinding>()
                    .Where(b => b.RulesetName == osu.Game.Rulesets.Osu.OsuRuleset.SHORT_NAME && b.Variant == 0);

                base.ReloadMappings(osuBindings);

                if (!AllowGameplayInputs)
                    KeyBindings = KeyBindings.Where(static b => b.GetAction<OsuAction>() == OsuAction.Smoke).ToList();
            }
        }
    }
}
