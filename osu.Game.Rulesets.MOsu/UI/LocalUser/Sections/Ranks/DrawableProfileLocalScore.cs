// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Extensions.LocalisationExtensions;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Cursor;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Online.Leaderboards;
using osu.Game.Overlays;
using osu.Game.Overlays.Profile.Sections;
using osu.Game.Resources.Localisation.Web;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;
using osu.Game.Scoring;
using osu.Game.Screens;
using osu.Game.Screens.Select;
using osu.Game.Utils;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.MOsu.UI.LocalUser.Sections.Ranks
{
    public partial class DrawableProfileLocalScore : CompositeDrawable
    {
        private const int height = 40;
        private const int performance_width = 100;

        private const float performance_background_shear = 0.45f;

        protected readonly ScoreInfo Score;
        private readonly Ruleset rulesetInstance;

        [Resolved]
        private OsuColour colours { get; set; } = null!;

        [Resolved]
        private OverlayColourProvider colourProvider { get; set; } = null!;

        public DrawableProfileLocalScore(ScoreInfo score, Ruleset rulesetInstance = null)
        {
            Score = score;
            this.rulesetInstance = rulesetInstance;

            RelativeSizeAxes = Axes.X;
            Height = height;
        }

        [BackgroundDependencyLoader]
        private void load()
        {
            var ruleset = rulesetInstance ?? throw new InvalidOperationException("Ruleset not provided");

            AddInternal(new ProfileItemContainer
            {
                Children = new Drawable[]
                {
                    new Container
                    {
                        RelativeSizeAxes = Axes.Both,
                        Padding = new MarginPadding { Left = 20, Right = performance_width },
                        Children = new Drawable[]
                        {
                            new FillFlowContainer
                            {
                                Anchor = Anchor.CentreLeft,
                                Origin = Anchor.CentreLeft,
                                AutoSizeAxes = Axes.Both,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(10, 0),
                                Children = new Drawable[]
                                {
                                    new UpdateableRank(Score.Rank)
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        Size = new Vector2(50, 20),
                                    },
                                    new FillFlowContainer
                                    {
                                        Anchor = Anchor.CentreLeft,
                                        Origin = Anchor.CentreLeft,
                                        AutoSizeAxes = Axes.Both,
                                        Direction = FillDirection.Vertical,
                                        Spacing = new Vector2(0, 2),
                                        Children = new Drawable[]
                                        {
                                            new ScoreBeatmapMetadataContainer(Score),
                                            new FillFlowContainer
                                            {
                                                AutoSizeAxes = Axes.Both,
                                                Direction = FillDirection.Horizontal,
                                                Spacing = new Vector2(15, 0),
                                                Children = new Drawable[]
                                                {
                                                    new OsuSpriteText
                                                    {
                                                        Text = $"{Score.BeatmapInfo?.DifficultyName ?? "Unknown"}",
                                                        Font = OsuFont.GetFont(size: 12, weight: FontWeight.Regular),
                                                        Colour = colours.Yellow
                                                    },
                                                    new DrawableDate(Score.Date, 12)
                                                    {
                                                        Colour = colourProvider.Foreground1
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Child = CreateScoreAndHitStatistics()
                            },
                            new FillFlowContainer
                            {
                                Anchor = Anchor.CentreRight,
                                Origin = Anchor.CentreRight,
                                AutoSizeAxes = Axes.X,
                                RelativeSizeAxes = Axes.Y,
                                Direction = FillDirection.Horizontal,
                                Spacing = new Vector2(15),
                                Children = new Drawable[]
                                {
                                    new Container
                                    {
                                        AutoSizeAxes = Axes.X,
                                        RelativeSizeAxes = Axes.Y,
                                        Padding = new MarginPadding { Horizontal = 10, Vertical = 5 },
                                        Anchor = Anchor.CentreRight,
                                        Origin = Anchor.CentreRight,
                                        Child = CreateRightContent()
                                    },
                                    new FillFlowContainer
                                    {
                                        AutoSizeAxes = Axes.Both,
                                        Anchor = Anchor.CentreRight,
                                        Origin = Anchor.CentreRight,
                                        Direction = FillDirection.Horizontal,
                                        Spacing = new Vector2(2),
                                        Children = Score.Mods.AsOrdered().Select(mod => new ModIcon(mod)
                                        {
                                            Scale = new Vector2(0.35f)
                                        }).ToList(),
                                    }
                                }
                            }
                        }
                    },
                    new Container
                    {
                        RelativeSizeAxes = Axes.Y,
                        Width = performance_width,
                        Anchor = Anchor.CentreRight,
                        Origin = Anchor.CentreRight,
                        Children = new Drawable[]
                        {
                            new Box
                            {
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                RelativeSizeAxes = Axes.Both,
                                Height = 0.5f,
                                Colour = colourProvider.Background3,
                                Shear = new Vector2(-performance_background_shear, 0),
                                EdgeSmoothness = new Vector2(2, 0),
                            },
                            new Box
                            {
                                Anchor = Anchor.TopRight,
                                Origin = Anchor.TopRight,
                                RelativeSizeAxes = Axes.Both,
                                RelativePositionAxes = Axes.Y,
                                Height = -0.5f,
                                Position = new Vector2(0, 1),
                                Colour = colourProvider.Background3,
                                Shear = new Vector2(performance_background_shear, 0),
                                EdgeSmoothness = new Vector2(2, 0),
                            },
                            new Container
                            {
                                RelativeSizeAxes = Axes.Both,
                                Padding = new MarginPadding
                                {
                                    Vertical = 5,
                                    Left = 30,
                                    Right = 20
                                },
                                Child = createDrawablePerformance().With(d =>
                                {
                                    d.Anchor = Anchor.Centre;
                                    d.Origin = Anchor.Centre;
                                })
                            }
                        }
                    }
                }
            });
        }

        protected virtual Drawable CreateRightContent() => CreateDrawableAccuracy();

        protected virtual Drawable CreateScoreAndHitStatistics() => new FillFlowContainer
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            AutoSizeAxes = Axes.Both,
            Direction = FillDirection.Horizontal,
            Spacing = new Vector2(4, 0),
            Children = new Drawable[]
            {
                new OsuSpriteText { Text = Score.TotalScore.ToString("N0"), Font = OsuFont.GetFont(size: 14, weight: FontWeight.Bold), Colour = colourProvider.Foreground1 },
                new OsuSpriteText { Text = "/", Font = OsuFont.GetFont(size: 14), Colour = colourProvider.Light3 },
                new OsuSpriteText { Text = $"{Score.Combo}x", Font = OsuFont.GetFont(size: 14, weight: FontWeight.Bold), Colour = colourProvider.Foreground1 },
                new OsuSpriteText { Text = $"({Score.MaxCombo}x)", Font = OsuFont.GetFont(size: 14), Colour = colourProvider.Light3 },
                new OsuSpriteText { Text = "|", Font = OsuFont.GetFont(size: 14), Colour = colourProvider.Light3 },
                createHitCount(Score.Statistics.ContainsKey(HitResult.Great) ? Score.Statistics[HitResult.Great] : 0, colours.GreenLight),
                new OsuSpriteText { Text = "/", Font = OsuFont.GetFont(size: 14), Colour = colourProvider.Light3 },
                createHitCount(Score.Statistics.ContainsKey(HitResult.Good) ? Score.Statistics[HitResult.Good] : 0, colours.Orange1),
                new OsuSpriteText { Text = "/", Font = OsuFont.GetFont(size: 14), Colour = colourProvider.Light3 },
                createHitCount(Score.Statistics.ContainsKey(HitResult.Ok) ? Score.Statistics[HitResult.Ok] : 0, colours.Orange2),
                new OsuSpriteText { Text = "/", Font = OsuFont.GetFont(size: 14), Colour = colourProvider.Light3 },
                createHitCount(Score.Statistics.ContainsKey(HitResult.Miss) ? Score.Statistics[HitResult.Miss] : 0, colours.Red)
            }
        };

        private Drawable createHitCount(int count, Color4 colour)
        {
            return new OsuSpriteText
            {
                Text = count.ToString(),
                Font = OsuFont.GetFont(size: 14, weight: FontWeight.Bold),
                Colour = count > 0 ? colour : colourProvider.Background2
            };
        }

        protected Drawable CreateDrawableAccuracy() => new Container
        {
            Width = 65,
            RelativeSizeAxes = Axes.Y,
            Child = new OsuSpriteText
            {
                Text = Score.Accuracy.FormatAccuracy(),
                Font = OsuFont.GetFont(size: 14, weight: FontWeight.Bold, italics: true),
                Colour = colours.Yellow,
                Anchor = Anchor.CentreLeft,
                Origin = Anchor.CentreLeft
            }
        };

        private Drawable createDrawablePerformance()
        {
            if (Score.PP == null)
            {
                return new SpriteTextWithTooltip
                {
                    Text = "-",
                    Font = OsuFont.GetFont(weight: FontWeight.Bold),
                    TooltipText = "PP not calculated for this score",
                    Colour = colourProvider.Highlight1
                };
            }

            var ppTooltipText = LocalisableString.Interpolate($@"{Score.PP:N1}pp");

            return new FillFlowContainer
            {
                AutoSizeAxes = Axes.Both,
                Direction = FillDirection.Horizontal,
                Children = new[]
                {
                    new SpriteTextWithTooltip
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Font = OsuFont.GetFont(weight: FontWeight.Bold),
                        Text = Score.PP.Value.ToLocalisableString(@"N0"),
                        TooltipText = ppTooltipText,
                        Colour = colourProvider.Highlight1,
                    },
                    new SpriteTextWithTooltip
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Font = OsuFont.GetFont(weight: FontWeight.Bold).With(size: 12),
                        Text = @"pp",
                        TooltipText = ppTooltipText,
                        Colour = colourProvider.Light3,
                    }
                }
            };
        }

        private partial class ScoreBeatmapMetadataContainer : OsuHoverContainer, IHasContextMenu
        {
            IScoreInfo ScoreInfo;
            private readonly IBeatmapInfo? beatmap;

            [Resolved(CanBeNull = true)]
            private BeatmapManager BeatmapManager {get; set;}

            [Resolved(CanBeNull = true)]
            private IPerformFromScreenRunner? performer { get; set; }

            [Resolved(CanBeNull = true)]
            private DifficultyRecommender difficultyRecommender { get; set; }

            [Resolved(CanBeNull = true)]
            private ScoreManager scoreManager { get; set; }

            [Resolved(CanBeNull = true)]
            private LocalUserManager localUserManager { get; set; }
            [Resolved(CanBeNull = true)]
            private LocalUserProfileOverlay? overlay { get; set; }


            public MenuItem[] ContextMenuItems
            {
                get
                {
                    return new MenuItem[]
                    {
                        new OsuMenuItem("Delete", MenuItemType.Destructive, async () =>
                        {
                            if (scoreManager == null) return;
                            scoreManager.Delete((ScoreInfo)ScoreInfo);
                            if (localUserManager != null)
                                await localUserManager.UpdateUserStatisticsAsync((RulesetInfo)ScoreInfo.Ruleset);
                            overlay?.fetchAndSetContentForLocalUser(ScoreInfo.User,ScoreInfo.Ruleset);
                        })
                    };
                }
            }

            public ScoreBeatmapMetadataContainer(IScoreInfo scoreInfo)
            {
                ScoreInfo = scoreInfo;
                beatmap = scoreInfo.Beatmap;
                AutoSizeAxes = Axes.Both;
            }


            [BackgroundDependencyLoader]
            private void load()
            {
                if (beatmap == null)
                {
                    Child = new OsuSpriteText
                    {
                        Text = "Beatmap either updated or removed",
                        Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold, italics: true)
                    };
                    return;
                }

                Action = () =>
                {
                    PresentBeatmap(beatmap.BeatmapSet, s => beatmap.Equals(s));
                    overlay?.Hide();
                };

                Child = new FillFlowContainer
                {
                    AutoSizeAxes = Axes.Both,
                    Children = CreateText(beatmap),
                };
            }

            public void PresentBeatmap(IBeatmapSetInfo beatmapSet, Predicate<BeatmapInfo> difficultyCriteria = null)
            {
                if (BeatmapManager == null) return;

                Logger.Log($"Beginning {nameof(PresentBeatmap)} with beatmap {beatmapSet}");
                Live<BeatmapSetInfo> databasedSet = null;

                if (beatmapSet.OnlineID > 0)
                    databasedSet = BeatmapManager.QueryBeatmapSet(s => s.OnlineID == beatmapSet.OnlineID && !s.DeletePending);

                if (beatmapSet is BeatmapSetInfo localBeatmap)
                    databasedSet ??= BeatmapManager.QueryBeatmapSet(s => s.Hash == localBeatmap.Hash && !s.DeletePending);

                if (databasedSet == null)
                {
                    Logger.Log("The requested beatmap could not be loaded.", LoggingTarget.Information);
                    return;
                }

                var detachedSet = databasedSet.PerformRead(s => s.Detach());

                if (detachedSet.DeletePending)
                {
                    Logger.Log("The requested beatmap has since been deleted.", LoggingTarget.Information);
                    return;
                }

                performer?.PerformFromScreen(screen =>
                {
                    var beatmaps = detachedSet.Beatmaps.Where(b => difficultyCriteria?.Invoke(b) ?? true).ToList();

                    if (beatmaps.Count == 0)
                        beatmaps = detachedSet.Beatmaps.ToList();

                    var selection = beatmaps.FirstOrDefault(b => b.Ruleset.Equals(ScoreInfo.Ruleset)) ?? beatmaps.First();

                    if (screen is IHandlePresentBeatmap presentableScreen)
                    {
                        presentableScreen.PresentBeatmap(BeatmapManager.GetWorkingBeatmap(selection), (RulesetInfo)ScoreInfo.Ruleset);
                    }
                    else
                    {
                        Logger.Log($"Completing {nameof(PresentBeatmap)} with beatmap {beatmapSet} (maintaining ruleset)");
                    }
                }, validScreens: new[]
                {
                    typeof(SongSelect), typeof(Screens.Select.SongSelect), typeof(IHandlePresentBeatmap)
                });
            }

            private Drawable[] CreateText(IBeatmapInfo beatmapInfo)
            {
                var metadata = beatmapInfo.Metadata;

                return new Drawable[]
                {
                    new OsuSpriteText
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Text = new RomanisableString(metadata.TitleUnicode, metadata.Title),
                        Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold, italics: true)
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Text = " by ",
                        Font = OsuFont.GetFont(size: 12, italics: true)
                    },
                    new OsuSpriteText
                    {
                        Anchor = Anchor.BottomLeft,
                        Origin = Anchor.BottomLeft,
                        Text = new RomanisableString(metadata.ArtistUnicode, metadata.Artist),
                        Font = OsuFont.GetFont(size: 12, italics: true)
                    },
                };
            }
        }
    }

    internal partial class SpriteTextWithTooltip : OsuSpriteText, IHasTooltip
    {
        public LocalisableString TooltipText { get; set; }
    }
}
