// Compose timeline lines for hitobject gimmicks (forced no-approach-circle),
// ported from delta fork
// (osu.Game/Screens/Edit/Compose/Components/Timeline/TimelineHitObjectGimmickLineDisplay.cs).
// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Graphics;
using osu.Game.Rulesets.MOsu.Delta.Gimmicks;
using osu.Game.Screens.Edit.Components.Timelines.Summary.Parts;
using osuTK.Graphics;

namespace osu.Game.Rulesets.MOsu.Delta.Edit.Timeline
{
    public partial class TimelineHitObjectGimmickLineDisplay : TimelinePart
    {
        private readonly BeatmapHitObjectGimmicks gimmicks;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        public TimelineHitObjectGimmickLineDisplay(BeatmapHitObjectGimmicks gimmicks)
        {
            this.gimmicks = gimmicks;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Refresh();
        }

        /// <summary>
        /// Rebuilds the no-approach lines from the current entries (fired when the entries change).
        /// </summary>
        public void Refresh()
        {
            Clear();

            var entries = gimmicks.Entries.Where(e => e.Settings?.ForceNoApproachCircle == true)
                                        .OrderBy(e => e.StartTime)
                                        .ThenBy(e => e.ComboIndexWithOffsets)
                                        .ToList();

            if (entries.Count == 0)
                return;

            foreach (var entry in entries)
                addLine(entry.StartTime, colours.Blue2, 2f, 0.95f);
        }

        private void addLine(double time, Color4 colour, float width, float alpha)
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
