// Compose timeline section-span bars, ported from delta fork
// (osu.Game/Screens/Edit/Compose/Components/Timeline/TimelineSectionGimmickDisplay.cs).
// Unlike delta, the editor model cannot be DI-resolved from the timeline subtree
// (mosu caches it in the composer's dependency container), so it is injected directly.
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Bindables;
using osu.Framework.Caching;
using osu.Framework.Graphics;
using osu.Game.Rulesets.MOsu.Delta.Gimmicks;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Edit.Components.Timelines.Summary.Parts;

namespace osu.Game.Rulesets.MOsu.Delta.Edit.Timeline
{
    public partial class TimelineSectionGimmickDisplay : TimelinePart<TimelineSectionGimmick>
    {
        private readonly Cached displayCache = new Cached();

        private readonly DeltaSectionGimmickEditorModel editorModel;

        private readonly BindableList<SectionGimmickSection> sections = new BindableList<SectionGimmickSection>();

        public TimelineSectionGimmickDisplay(DeltaSectionGimmickEditorModel editorModel)
        {
            this.editorModel = editorModel;
        }

        protected override void LoadBeatmap(EditorBeatmap beatmap)
        {
            base.LoadBeatmap(beatmap);

            sections.UnbindAll();
            sections.BindTo(editorModel.Sections);
            sections.BindCollectionChanged((_, e) =>
            {
                if (e.Action != NotifyCollectionChangedAction.Replace)
                    displayCache.Invalidate();
            });

            editorModel.SelectedSectionId.BindValueChanged(_ => updateSelectionState());

            displayCache.Invalidate();
        }

        protected override void Update()
        {
            base.Update();

            if (!displayCache.IsValid)
            {
                recreateDisplay();
                displayCache.Validate();
            }
        }

        private void recreateDisplay()
        {
            Clear();

            foreach (var section in sections.OrderBy(s => s.StartTime))
            {
                if (section.EndTime < 0)
                    continue;

                Add(new TimelineSectionGimmick(section));
            }

            updateSelectionState();
        }

        private void updateSelectionState()
        {
            foreach (var section in AliveChildren)
                section.SetSelected(section.Section.Id == editorModel.SelectedSectionId.Value);
        }
    }
}
