// Compose timeline section boundary lines (start/end), ported from delta fork
// (osu.Game/Screens/Edit/Compose/Components/Timeline/TimelineSectionGimmickBoundaryLineDisplay.cs).
// Intentionally simple and high-contrast for guaranteed visibility.
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Caching;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Rulesets.MOsu.Delta.Gimmicks;
using osu.Game.Screens.Edit;
using osu.Game.Screens.Edit.Components.Timelines.Summary.Parts;
using osuTK.Graphics;

namespace osu.Game.Rulesets.MOsu.Delta.Edit.Timeline
{
    /// <summary>
    /// Draws explicit section boundary lines (start and end) exactly like timeline marker lines.
    /// </summary>
    public partial class TimelineSectionGimmickBoundaryLineDisplay : TimelinePart
    {
        private readonly Cached displayCache = new Cached();

        private readonly DeltaSectionGimmickEditorModel editorModel;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        public TimelineSectionGimmickBoundaryLineDisplay(DeltaSectionGimmickEditorModel editorModel)
        {
            this.editorModel = editorModel;
        }

        protected override void LoadBeatmap(EditorBeatmap beatmap)
        {
            base.LoadBeatmap(beatmap);

            editorModel.Sections.BindCollectionChanged((_, e) =>
            {
                if (e.Action != NotifyCollectionChangedAction.Replace)
                    displayCache.Invalidate();
            });

            displayCache.Invalidate();
        }

        protected override void Update()
        {
            base.Update();

            if (!displayCache.IsValid)
            {
                recreate();
                displayCache.Validate();
            }
        }

        private void recreate()
        {
            Clear();

            var sections = editorModel.Sections;

            if (sections.Count == 0)
                return;

            var usedEndTimes = new HashSet<double>();

            foreach (var section in sections.OrderBy(s => s.StartTime))
            {
                addBoundaryLine(section.StartTime, colours.Yellow, 4f, 1f);

                if (section.EndTime >= 0 && usedEndTimes.Add(section.EndTime))
                    addBoundaryLine(section.EndTime, colours.Orange1, 3f, 0.9f);
            }
        }

        private void addBoundaryLine(double time, Color4 colour, float width, float alpha)
        {
            Add(new Container
            {
                RelativePositionAxes = Axes.X,
                RelativeSizeAxes = Axes.Y,
                X = (float)time,
                Width = 1,
                Children = new Drawable[]
                {
                    new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = width + 2,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Colour = Color4.Black,
                        Alpha = Math.Min(0.7f, alpha * 0.7f),
                    },
                    new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = width,
                        Anchor = Anchor.Centre,
                        Origin = Anchor.Centre,
                        Colour = colour,
                        Alpha = alpha,
                    },
                }
            });
        }
    }
}
