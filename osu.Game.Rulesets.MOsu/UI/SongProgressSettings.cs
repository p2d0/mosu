// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Extensions.Color4Extensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Colour;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Input.Events;
using osu.Framework.Localisation;
using osu.Framework.Utils;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Objects;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.PlayerSettings;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.MOsu.UI
{
    public partial class SongProgressSettings : PlayerSettingsGroup
    {
        private const int display_granularity = 200;
        private const int tier_count = 5;

        private readonly FillFlowContainer content;
        private readonly ProgressBar progressBar;
        private readonly OsuSpriteText timeCurrent;
        private readonly OsuSpriteText timeTotal;
        private readonly SegmentedGraph<int> graph;
        private readonly Container graphContainer;

        private double startTime;
        private double endTime;
        private double length => endTime - startTime;

        private readonly IBindable<bool> isPaused = new BindableBool();

        [Resolved]
        private ReplayPlayer replayPlayer { get; set; } = null!;

        [Resolved]
        private GameplayClockContainer gameplayClock { get; set; } = null!;

        [Resolved]
        private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

        public SongProgressSettings()
            : base("Song Progress")
        {
            Children = new Drawable[]
            {
                content = new FillFlowContainer
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 10),
                    Children = new Drawable[]
                    {
                        new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Children = new Drawable[]
                            {
                                progressBar = new ProgressBar
                                {
                                    Name = "Seek bar",
                                    RelativeSizeAxes = Axes.X,
                                },
                                graphContainer = new Container
                                {
                                    Anchor = Anchor.BottomLeft,
                                    Origin = Anchor.BottomLeft,
                                    Masking = true,
                                    CornerRadius = 5,
                                    Child = graph = new SegmentedGraph<int>(tier_count)
                                    {
                                        Name = "Difficulty graph",
                                        RelativeSizeAxes = Axes.Both,
                                        Blending = BlendingParameters.Additive,
                                    },
                                    RelativeSizeAxes = Axes.X,
                                },
                            },
                        },
                        new FillFlowContainer
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Horizontal,
                            Children = new Drawable[]
                            {
                                timeCurrent = new OsuSpriteText
                                {
                                    Font = OsuFont.Numeric.With(size: 12),
                                    Colour = Colour4.White,
                                },
                                new OsuSpriteText
                                {
                                    Text = " / ",
                                    Font = OsuFont.GetFont(size: 12),
                                    Colour = Colour4.White.Opacity(0.5f),
                                    Margin = new MarginPadding { Horizontal = 4 },
                                },
                                timeTotal = new OsuSpriteText
                                {
                                    Font = OsuFont.Numeric.With(size: 12),
                                    Colour = Colour4.White.Opacity(0.5f),
                                },
                            },
                        },
                    },
                },
            };
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var hitObjects = beatmap.Value.Beatmap.HitObjects;
            startTime = hitObjects[0].StartTime;
            endTime = hitObjects[^1].StartTime;

            progressBar.StartTime = startTime;
            progressBar.EndTime = endTime;

            // Build density graph
            int[] values = new int[display_granularity];
            double interval = length / display_granularity;

            foreach (var h in hitObjects)
            {
                double endTime = h.GetEndTime();
                int startRange = (int)((h.StartTime - startTime) / interval);
                int endRange = (int)((endTime - startTime) / interval);
                for (int i = startRange; i <= endRange; i++)
                {
                    if (i >= 0 && i < display_granularity)
                        values[i]++;
                }
            }

            graph.Values = values;

            var colours = new List<Colour4>();
            for (int i = 0; i < tier_count; i++)
                colours.Add(OsuColour.Gray(0.2f).Opacity(0.1f));
            graph.TierColours = colours;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            isPaused.BindTo(gameplayClock.IsPaused);
        }

        protected override void Dispose(bool isDisposing)
        {
            base.Dispose(isDisposing);
            isPaused.UnbindAll();
        }

        protected override void Update()
        {
            base.Update();

            graphContainer.Height = progressBar.Height;

            double currentTime = gameplayClock.CurrentTime;
            double clampedTime = Math.Clamp(currentTime - startTime, 0, length);
            double progress = length > 0 ? clampedTime / length : 0;

            progressBar.FillWidth = (float)progress;

            timeCurrent.Text = formatTime(TimeSpan.FromMilliseconds(currentTime - startTime));
            timeTotal.Text = formatTime(TimeSpan.FromMilliseconds(length));
        }

        private string formatTime(TimeSpan timeSpan)
        {
            var abs = timeSpan.Duration();
            var sign = timeSpan < TimeSpan.Zero ? "-" : "";
            return $"{sign}{(int)abs.TotalMinutes:D2}:{abs.Seconds:D2}";
        }

        private partial class ProgressBar : Container
        {
            private readonly Box background;
            private readonly Box fill;

            private float fillWidth;
            public float FillWidth
            {
                get => fillWidth;
                set
                {
                    fillWidth = Math.Clamp(value, 0, 1);
                    fill.Width = fillWidth * DrawWidth;
                }
            }

            public double StartTime { get; set; }
            public double EndTime { get; set; }
            private double length => EndTime - StartTime;

            [Resolved]
            private ReplayPlayer replayPlayer { get; set; } = null!;

            public ProgressBar()
            {
                Height = 8;
                Masking = true;
                CornerRadius = 4;
                Children = new Drawable[]
                {
                    background = new Box
                    {
                        RelativeSizeAxes = Axes.Both,
                        Colour = Color4.White.Opacity(0.15f),
                    },
                    fill = new Box
                    {
                        RelativeSizeAxes = Axes.Y,
                        Colour = Color4.White.Opacity(0.8f),
                    },
                };
            }

            protected override bool OnClick(ClickEvent e)
            {
                float clickProgress = Math.Clamp(e.MouseDownPosition.X, 0, DrawWidth) / DrawWidth;
                double seekTime = StartTime + length * clickProgress;
                replayPlayer.Seek(seekTime);
                return true;
            }

            protected override bool OnMouseDown(MouseDownEvent e)
            {
                float clickProgress = Math.Clamp(e.MouseDownPosition.X, 0, DrawWidth) / DrawWidth;
                double seekTime = StartTime + length * clickProgress;
                replayPlayer.Seek(seekTime);
                return true;
            }

            protected override bool OnMouseMove(MouseMoveEvent e)
            {
                if (e.HasAnyButtonPressed)
                {
                    float clickProgress = Math.Clamp(e.MousePosition.X, 0, DrawWidth) / DrawWidth;
                    double seekTime = StartTime + length * clickProgress;
                    replayPlayer.Seek(seekTime);
                }
                return false;
            }
        }
    }
}
