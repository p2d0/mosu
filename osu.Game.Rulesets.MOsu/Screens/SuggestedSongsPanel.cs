using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables.Cards;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.MOsu.Graphics.UserInterface;
using osu.Game.Rulesets.MOsu.Utils;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Overlays;
using osu.Game.Overlays.BeatmapListing;
using osu.Game.Rulesets.MOsu.Configuration;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Screens
{
    public partial class SuggestedSongsPanel : Container
    {
        private ReverseChildIDFillFlowContainer<BeatmapCard> spotlightGrid = null!;
        private ReverseChildIDFillFlowContainer<BeatmapCard> suggestionsGrid = null!;
        private ReverseChildIDFillFlowContainer<BeatmapCard> artistGrid = null!;
        private LoadingLayer spotlightLoading = null!;
        private LoadingLayer suggestionsLoading = null!;
        private LoadingLayer artistLoading = null!;
        private DifficultyRangeSlider starSlider = null!;
        private CancellationTokenSource debounceSource = null!;
        private int requestSequence;
        private int pendingOnlineID;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        [Resolved]
        private IRulesetConfigCache configCache { get; set; } = null!;

        [Resolved]
        private RulesetStore rulesets { get; set; } = null!;

        private RulesetInfo ruleset = null!;
        private readonly ScoreInfo score;
        private readonly Random random = new Random();

        private class LocalLookupData
        {
            public HashSet<int> OnlineIDs = null!;
            public HashSet<(string, string)> TitleArtists = null!;
        }

        private Task<LocalLookupData> localLookupTask = null!;

        public SuggestedSongsPanel(ScoreInfo score)
        {
            this.score = score;
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();

            ruleset = rulesets.GetRuleset(score.BeatmapInfo?.Ruleset.ShortName ?? "osu") ?? rulesets.AvailableRulesets.First();

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;

            InternalChildren = new Drawable[]
            {
                new ReverseChildIDFillFlowContainer<Drawable>
                {
                    RelativeSizeAxes = Axes.X,
                    AutoSizeAxes = Axes.Y,
                    Direction = FillDirection.Vertical,
                    Spacing = new Vector2(0, 15),
                    Children = new Drawable[]
                    {
                        starSlider = new DifficultyRangeSlider
                        {
                            RelativeSizeAxes = Axes.X,
                            MinRange = 0,
                        },
                        new OsuSpriteText
                        {
                            Text = "Spotlight",
                            Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                            Colour = OsuColour.Gray(0.7f),
                        },
                        spotlightLoading = new LoadingLayer
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 100,
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            State = { Value = Visibility.Visible },
                        },
                        spotlightGrid = new ReverseChildIDFillFlowContainer<BeatmapCard>
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Full,
                            Spacing = new Vector2(6),
                            Alpha = 0,
                        },
                        new OsuSpriteText
                        {
                            Text = "Similar Beats",
                            Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                            Colour = OsuColour.Gray(0.7f),
                        },
                        suggestionsLoading = new LoadingLayer
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 100,
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            State = { Value = Visibility.Visible },
                        },
                        suggestionsGrid = new ReverseChildIDFillFlowContainer<BeatmapCard>
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Full,
                            Spacing = new Vector2(6),
                            Alpha = 0,
                        },
                        new OsuSpriteText
                        {
                            Text = "Same Artist",
                            Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                            Colour = OsuColour.Gray(0.7f),
                        },
                        artistLoading = new LoadingLayer
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 100,
                            Anchor = Anchor.TopLeft,
                            Origin = Anchor.TopLeft,
                            State = { Value = Visibility.Visible },
                        },
                        artistGrid = new ReverseChildIDFillFlowContainer<BeatmapCard>
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Full,
                            Spacing = new Vector2(6),
                            Alpha = 0,
                        },
                    },
                },
            };

            if (!api.IsLoggedIn)
            {
                Logger.Log("[MOsu] SuggestedSongsPanel: user not logged in, hiding", LoggingTarget.Runtime);
                this.Hide();
                this.AutoSizeAxes = Axes.None;
                this.Height = 0;
                return;
            }
            Logger.Log("[MOsu] SuggestedSongsPanel.LoadComplete", LoggingTarget.Runtime);

            var config = configCache.GetConfigFor(new MosuRuleset()) as MOsuRulesetConfigManager ?? throw new InvalidOperationException("MOsuRulesetConfigManager not found");
            starSlider.LowerBound = config.GetBindable<double>(MOsuRulesetSetting.SuggestedSongsMinStars);
            starSlider.UpperBound = config.GetBindable<double>(MOsuRulesetSetting.SuggestedSongsMaxStars);

            starSlider.LowerBound.ValueChanged += _ => scheduleReload();
            starSlider.UpperBound.ValueChanged += _ => scheduleReload();

            Schedule(() => fetchSuggestions());
        }

        private void scheduleReload()
        {
            debounceSource?.Cancel();
            debounceSource = new CancellationTokenSource();
            var token = debounceSource.Token;
            Task.Delay(500).ContinueWith(_ =>
            {
                if (!token.IsCancellationRequested)
                    Schedule(() => fetchSuggestions());
            });
        }

        private void fetchSuggestions()
        {
            requestSequence++;
            int currentSequence = requestSequence;

            // Clear grids and show loading spinners
            spotlightGrid.Clear();
            suggestionsGrid.Clear();
            artistGrid.Clear();
            spotlightGrid.Hide();
            suggestionsGrid.Hide();
            artistGrid.Hide();
            spotlightLoading.Show();
            suggestionsLoading.Show();
            artistLoading.Show();

            int onlineID = score.BeatmapInfo?.BeatmapSet?.OnlineID ?? 0;
            Logger.Log($"[MOsu] SuggestedSongsPanel: fetching set {onlineID}", LoggingTarget.Runtime);
            pendingOnlineID = onlineID;

            // Extract tags on UI thread before going background
            var localBeatmap = score.BeatmapInfo?.BeatmapSet?.Beatmaps.FirstOrDefault();
            var rawTags = localBeatmap?.Metadata.Tags ?? "";

            // Start local lookup immediately (runs in parallel with API)
            localLookupTask = Task.Run(() =>
            {
                var localSets = beatmapManager.GetAllUsableBeatmapSets();
                return new LocalLookupData
                {
                    OnlineIDs = localSets.Where(b => b.OnlineID > 0).Select(b => b.OnlineID).ToHashSet(),
                    TitleArtists = localSets.Select(b => (b.Metadata.Title.ToLowerInvariant(), b.Metadata.Artist.ToLowerInvariant())).ToHashSet()
                };
            });

            // Fetch current beatmap info
            var getSetRequest = new GetBeatmapSetRequest(onlineID);
            getSetRequest.Success += response =>
            {
                Logger.Log($"[MOsu] GetBeatmapSetRequest success: genre={response.Genre.Name}, stars={response.Beatmaps.Max(b => b.StarRating)}", LoggingTarget.Runtime);
                if (currentSequence != requestSequence) return;

                var genre = SuggestionQueryBuilder.ToSearchGenre(response.Genre);
                var language = SuggestionQueryBuilder.ToSearchLanguage(response.Language);
                double bpm = response.Beatmaps.Max(b => b.BPM);
                double minBpm = bpm - 10;
                double maxBpm = bpm + 10;

                double minStars = starSlider.LowerBound.Value;
                double maxStars = starSlider.UpperBound.Value;
                bool maxStarsIsDefault = starSlider.UpperBound.IsDefault;

                var query = SuggestionQueryBuilder.BuildMainQuery(minBpm, maxBpm, minStars, maxStars, maxStarsIsDefault, rawTags);

                // Queue all three requests — each populates independently
                queueSearchRequest(query, new[] { SearchGeneral.Spotlights }, genre, language, spotlightGrid, spotlightLoading, currentSequence);
                queueSearchRequest(query, null, genre, language, suggestionsGrid, suggestionsLoading, currentSequence);

                var artistQuery = SuggestionQueryBuilder.BuildArtistQuery(response.Artist, minStars, maxStars, maxStarsIsDefault);

                queueSearchRequest(artistQuery, null, genre, SearchLanguage.Any, artistGrid, artistLoading, currentSequence);
            };
            getSetRequest.Failure += e =>
            {
                Logger.Log($"[MOsu] GetBeatmapSetRequest failed: {e}", LoggingTarget.Runtime);
                Schedule(() =>
                {
                    spotlightLoading.Hide();
                    suggestionsLoading.Hide();
                    artistLoading.Hide();
                });
            };
            api.Queue(getSetRequest);
        }

        private void queueSearchRequest(string query, SearchGeneral[]? general, SearchGenre genre, SearchLanguage language, ReverseChildIDFillFlowContainer<BeatmapCard> grid, LoadingLayer loading, int currentSequence)
        {
            var sortCriteria = random.Next(4) switch
            {
                0 => SortCriteria.Title,
                1 => SortCriteria.Artist,
                2 => SortCriteria.Updated,
                _ => SortCriteria.Relevance
            };
            var sortDirection = random.Next(2) == 0 ? SortDirection.Descending : SortDirection.Ascending;

            var request = new SearchBeatmapSetsRequest(
                query: query,
                ruleset: ruleset,
                general: general,
                searchCategory: SearchCategory.Any,
                sortCriteria: sortCriteria,
                sortDirection: sortDirection,
                genre: genre,
                language: language,
                extra: null,
                ranks: null,
                played: SearchPlayed.Any,
                explicitContent: SearchExplicit.Show);

            request.Success += response =>
            {
                Logger.Log($"[MOsu] Search success: {response.BeatmapSets.Count()} results", LoggingTarget.Runtime);

                Task.Run(async () =>
                {
                    var localData = await localLookupTask;

                    var filtered = response.BeatmapSets
                        .Where(b => b.OnlineID != pendingOnlineID)
                        .Where(b => b.OnlineID <= 0 || !localData.OnlineIDs.Contains(b.OnlineID))
                        .Where(b => !localData.TitleArtists.Contains((b.Title.ToLowerInvariant(), b.Artist.ToLowerInvariant())))
                        .ToList();

                    if (filtered.Count > 12)
                    {
                        for (int i = filtered.Count - 1; i > 0; i--)
                        {
                            int j = random.Next(i + 1);
                            (filtered[i], filtered[j]) = (filtered[j], filtered[i]);
                        }
                    }

                    var finalResults = filtered.Take(12).ToList();
                    Logger.Log($"[MOsu] populateGrid: {filtered.Count} filtered, showing {finalResults.Count}", LoggingTarget.Runtime);

                    Schedule(() =>
                    {
                        if (currentSequence != requestSequence) return;

                        foreach (var set in finalResults)
                        {
                            var card = new CompactBeatmapCard(set, allowExpansion: true);
                            card.SetWidthForParent(grid.Parent.DrawWidth);
                            grid.Add(card);
                        }

                        loading.Hide();
                        grid.FadeIn(300, Easing.OutQuint);
                    });
                });
            };
            request.Failure += e =>
            {
                Logger.Log($"[MOsu] Search failed: {e}", LoggingTarget.Runtime);
                Schedule(() => loading.Hide());
            };
            api.Queue(request);
        }

    }
}
