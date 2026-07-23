using System;
using System.Linq;
using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables.Cards;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.BeatmapListing;
using osu.Game.Rulesets;
using osu.Game.Scoring;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Screens
{
    public partial class SuggestedSongsPanel : Container
    {
        private ReverseChildIDFillFlowContainer<BeatmapCard> spotlightGrid;
        private ReverseChildIDFillFlowContainer<BeatmapCard> suggestionsGrid;
        private IAPIProvider api = null!;
        private RulesetInfo ruleset = null!;
        private BeatmapManager beatmapManager = null!;
        private readonly ScoreInfo score;

        public SuggestedSongsPanel(ScoreInfo score)
        {
            this.score = score;
        }

        [BackgroundDependencyLoader]
        private void load(IAPIProvider api, RulesetStore rulesets, BeatmapManager beatmapManager)
        {
            this.api = api;
            this.ruleset = rulesets.GetRuleset(score.BeatmapInfo?.Ruleset.ShortName ?? "osu") ?? rulesets.AvailableRulesets.First();
            this.beatmapManager = beatmapManager;

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
                        new OsuSpriteText
                        {
                            Text = "Spotlight",
                            Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                            Colour = OsuColour.Gray(0.7f),
                        },
                        spotlightGrid = new ReverseChildIDFillFlowContainer<BeatmapCard>
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Full,
                            Spacing = new Vector2(10),
                        },
                        new OsuSpriteText
                        {
                            Text = "Similar Beats",
                            Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                            Colour = OsuColour.Gray(0.7f),
                        },
                        suggestionsGrid = new ReverseChildIDFillFlowContainer<BeatmapCard>
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Full,
                            Spacing = new Vector2(10),
                            Padding = new MarginPadding { Bottom = 200 },
                        },
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Logger.Log("[MOsu] SuggestedSongsPanel.LoadComplete", LoggingTarget.Runtime);
            Schedule(() => fetchSuggestions());
        }

        private void fetchSuggestions()
        {
            int onlineID = score.BeatmapInfo.BeatmapSet.OnlineID;
            Logger.Log($"[MOsu] SuggestedSongsPanel: fetching set {onlineID}", LoggingTarget.Runtime);

            // Fetch current beatmap info
            var getSetRequest = new GetBeatmapSetRequest(onlineID);
            getSetRequest.Success += response =>
            {
                Logger.Log($"[MOsu] GetBeatmapSetRequest success: genre={response.Genre.Name}, stars={response.Beatmaps.Max(b => b.StarRating)}", LoggingTarget.Runtime);

                var genre = toSearchGenre(response.Genre);
                double starRating = response.Beatmaps.Max(b => b.StarRating);
                double minStars = starRating - 0.5;
                double maxStars = starRating + 0.5;

                var query = $"favourites>1 stars>={minStars} stars<={maxStars}";
                Logger.Log($"[MOsu] Search query: {query}", LoggingTarget.Runtime);

                // Spotlight search
                var spotlightRequest = new SearchBeatmapSetsRequest(
                    query: query,
                    ruleset: ruleset,
                    general: new[] { SearchGeneral.Spotlights },
                    searchCategory: SearchCategory.Any,
                    sortCriteria: SortCriteria.Updated,
                    sortDirection: SortDirection.Descending,
                    genre: genre,
                    language: SearchLanguage.Any,
                    extra: null,
                    ranks: null,
                    played: SearchPlayed.Any,
                    explicitContent: SearchExplicit.Hide);

                spotlightRequest.Success += spotlightResponse =>
                {
                    Logger.Log($"[MOsu] Spotlight search success: {spotlightResponse.BeatmapSets.Count()} results", LoggingTarget.Runtime);
                    Schedule(() => populateGrid(spotlightGrid, spotlightResponse.BeatmapSets, onlineID));
                };
                spotlightRequest.Failure += e => Logger.Log($"[MOsu] Spotlight search failed: {e}", LoggingTarget.Runtime);
                api.Queue(spotlightRequest);

                // Similar beats search
                var searchRequest = new SearchBeatmapSetsRequest(
                    query: query,
                    ruleset: ruleset,
                    general: null,
                    searchCategory: SearchCategory.Any,
                    sortCriteria: SortCriteria.Updated,
                    sortDirection: SortDirection.Descending,
                    genre: genre,
                    language: SearchLanguage.Any,
                    extra: null,
                    ranks: null,
                    played: SearchPlayed.Any,
                    explicitContent: SearchExplicit.Hide);

                searchRequest.Success += searchResponse =>
                {
                    Logger.Log($"[MOsu] Similar search success: {searchResponse.BeatmapSets.Count()} results", LoggingTarget.Runtime);
                    Schedule(() => populateGrid(suggestionsGrid, searchResponse.BeatmapSets, onlineID));
                };
                searchRequest.Failure += e => Logger.Log($"[MOsu] Similar search failed: {e}", LoggingTarget.Runtime);

                api.Queue(searchRequest);
            };
            getSetRequest.Failure += e => Logger.Log($"[MOsu] GetBeatmapSetRequest failed: {e}", LoggingTarget.Runtime);

            api.Queue(getSetRequest);
        }

        private static readonly System.Random random = new System.Random();

        private void populateGrid(ReverseChildIDFillFlowContainer<BeatmapCard> grid, IEnumerable<APIBeatmapSet> beatmapSets, int excludeOnlineID)
        {
            var downloadedIDs = beatmapManager.GetAllUsableBeatmapSets().Where(b => b.OnlineID > 0).Select(b => b.OnlineID).ToHashSet();

            var filtered = beatmapSets
                .Where(b => b.OnlineID != excludeOnlineID)
                .Where(b => !downloadedIDs.Contains(b.OnlineID))
                .ToList();

            // Randomize if more than 9 results
            if (filtered.Count > 9)
            {
                for (int i = filtered.Count - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    (filtered[i], filtered[j]) = (filtered[j], filtered[i]);
                }
            }

            var results = filtered.Take(9).ToList();
            Logger.Log($"[MOsu] populateGrid: {filtered.Count} filtered, showing {results.Count}", LoggingTarget.Runtime);

            foreach (var set in results)
            {
                var card = BeatmapCard.Create(set, BeatmapCardSize.Normal, allowExpansion: true);
                grid.Add(card);
            }
        }

        private static SearchGenre toSearchGenre(BeatmapSetOnlineGenre genre)
        {
            if (genre.Id <= 0)
                return SearchGenre.Any;

            return (SearchGenre)genre.Id;
        }
    }
}
