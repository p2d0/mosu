// MOsu's editor composer: uses the gimmick-aware editor ruleset for the compose
// playfield and adds the section/hitobject gimmick toolbox groups.

using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.MOsu.Gimmicks;
using osu.Game.Rulesets.Osu.Edit;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Edit;

namespace osu.Game.Rulesets.MOsu.Edit
{
    public partial class MosuHitObjectComposer : OsuHitObjectComposer
    {
        private IReadOnlyDependencyContainer parentDependencies = null!;
        private DependencyContainer dependencies = null!;

        [Cached]
        protected readonly SectionGimmickToolboxGroup SectionGimmickToolboxGroup = new SectionGimmickToolboxGroup();

        [Cached]
        protected readonly HitObjectGimmickToolboxGroup HitObjectGimmickToolboxGroup = new HitObjectGimmickToolboxGroup();

        public MosuHitObjectComposer(Ruleset ruleset)
            : base(ruleset)
        {
        }

        protected override IReadOnlyDependencyContainer CreateChildDependencies(IReadOnlyDependencyContainer parent)
        {
            // [Resolved] fields come back null in the editor context, so capture the parent chain
            // and resolve dependencies manually (same pattern the drawable ruleset uses).
            parentDependencies = parent;
            return dependencies = (DependencyContainer)base.CreateChildDependencies(parent);
        }

        [BackgroundDependencyLoader]
        private void load(EditorBeatmap editorBeatmap)
        {
            // Ensure gimmicks are parsed + applied before the toolbox models snapshot them.
            WorkingBeatmap? working = null;

            try
            {
                working = parentDependencies.Get<IBindable<WorkingBeatmap>>()?.Value;
            }
            catch
            {
            }

            MosuGimmickRuntime.EnsureApplied(editorBeatmap.PlayableBeatmap, working);

            dependencies.CacheAs(new MosuSectionGimmickEditorModel(editorBeatmap));
            dependencies.CacheAs(new HitObjectGimmickEditorModel(editorBeatmap));

            RightToolbox.AddRange(new Drawable[]
            {
                HitObjectGimmickToolboxGroup,
                SectionGimmickToolboxGroup,
            });
        }

        protected override DrawableRuleset<OsuHitObject> CreateDrawableRuleset(Ruleset ruleset, IBeatmap beatmap, IReadOnlyList<Mod> mods)
            => new MosuEditorDrawableRuleset(ruleset, beatmap, mods);
    }
}
