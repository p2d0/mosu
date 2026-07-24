using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics;
using osu.Game.Screens.Select;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables.Cards;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Containers;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.MOsu.Graphics.UserInterface;
using osu.Game.Graphics.Sprites;
using osu.Game.Online.API;
using osu.Game.Online.API.Requests;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Overlays;
using osu.Game.Overlays.BeatmapListing;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.MOsu.Configuration;
using osu.Game.Scoring;
using osuTK;
using Realms;

namespace osu.Game.Rulesets.MOsu.Screens
{
    public partial class SuggestedSongsPanel : Container
    {
        private ReverseChildIDFillFlowContainer<BeatmapCard> spotlightGrid;
        private ReverseChildIDFillFlowContainer<BeatmapCard> suggestionsGrid;
        private ReverseChildIDFillFlowContainer<BeatmapCard> artistGrid;
        private LoadingLayer spotlightLoading;
        private LoadingLayer suggestionsLoading;
        private LoadingLayer artistLoading;
        private DifficultyRangeSlider starSlider;
        private CancellationTokenSource debounceSource;
        private int requestSequence;
        private List<APIBeatmapSet> pendingSpotlightResults;
        private List<APIBeatmapSet> pendingSuggestionsResults;
        private List<APIBeatmapSet> pendingArtistResults;
        private int pendingRequests;
        private int pendingOnlineID;
        private int pendingSequence;
        private IAPIProvider api = null!;
        private RulesetInfo ruleset = null!;
        private BeatmapManager beatmapManager = null!;
        private IRulesetConfigCache configCache = null!;
        private RealmAccess realmAccess = null!;
        private readonly ScoreInfo score;

        public SuggestedSongsPanel(ScoreInfo score)
        {
            this.score = score;
        }

        [BackgroundDependencyLoader]
        private void load(IAPIProvider api, RulesetStore rulesets, BeatmapManager beatmapManager, IRulesetConfigCache configCache, RealmAccess realmAccess)
        {
            this.api = api;
            this.ruleset = rulesets.GetRuleset(score.BeatmapInfo?.Ruleset.ShortName ?? "osu") ?? rulesets.AvailableRulesets.First();
            this.beatmapManager = beatmapManager;
            this.configCache = configCache;
            this.realmAccess = realmAccess;

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
                            Padding = new MarginPadding { Bottom = 200 },
                            Alpha = 0,
                        },
                    },
                },
            };
        }

        protected override void LoadComplete()
        {
            base.LoadComplete();
            if (!api.IsLoggedIn)
            {
                Logger.Log("[MOsu] SuggestedSongsPanel: user not logged in, hiding", LoggingTarget.Runtime);
                this.Hide();
                this.AutoSizeAxes = Axes.None;
                this.Height = 0;
                return;
            }
            Logger.Log("[MOsu] SuggestedSongsPanel.LoadComplete", LoggingTarget.Runtime);

            var config = configCache.GetConfigFor(new OsuRuleset()) as MOsuRulesetConfigManager ?? throw new InvalidOperationException("MOsuRulesetConfigManager not found");
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

            // Clear grids
            spotlightGrid.Clear();
            suggestionsGrid.Clear();
            artistGrid.Clear();
            spotlightGrid.Hide();
            suggestionsGrid.Hide();
            artistGrid.Hide();
            spotlightLoading.Show();
            suggestionsLoading.Show();
            artistLoading.Show();

            int onlineID = score.BeatmapInfo.BeatmapSet.OnlineID;
            Logger.Log($"[MOsu] SuggestedSongsPanel: fetching set {onlineID}", LoggingTarget.Runtime);

            // Fetch current beatmap info
            var getSetRequest = new GetBeatmapSetRequest(onlineID);
            getSetRequest.Success += response =>
            {
                Logger.Log($"[MOsu] GetBeatmapSetRequest success: genre={response.Genre.Name}, stars={response.Beatmaps.Max(b => b.StarRating)}", LoggingTarget.Runtime);

                var genre = toSearchGenre(response.Genre);
                var language = toSearchLanguage(response.Language);
                double bpm = response.Beatmaps.Max(b => b.BPM);
                double minBpm = bpm - 10;
                double maxBpm = bpm + 10;

                // Star range from slider
                double minStars = starSlider.LowerBound.Value;
                double maxStars = starSlider.UpperBound.Value;
                string starFilter = "";
                if (minStars > 0)
                    starFilter += $" stars>={minStars}";
                if (!starSlider.UpperBound.IsDefault && maxStars > 0)
                    starFilter += $" stars<={maxStars}";

                // Extract genre tags from local beatmap
                var localBeatmap = score.BeatmapInfo.BeatmapSet.Beatmaps.FirstOrDefault();
                var rawTags = localBeatmap?.Metadata.Tags ?? "";
                Logger.Log($"[MOsu] Raw tags: {rawTags}", LoggingTarget.Runtime);
                var genreTags = extractGenreTags(rawTags);
                Logger.Log($"[MOsu] Matched genre tags: {string.Join(", ", genreTags)} (count={genreTags.Count})", LoggingTarget.Runtime);
                var genreQuery = genreTags.Count > 0 ? " " + string.Join(" ", genreTags.Select(t => $"\"{t}\"")) : "";

                var query = $"favourites>1 bpm>={minBpm} bpm<={maxBpm}{starFilter}{genreQuery}";
                Logger.Log($"[MOsu] Search query: {query}", LoggingTarget.Runtime);

                // Collect results from all three requests, then do one Realm query
                pendingSpotlightResults = null;
                pendingSuggestionsResults = null;
                pendingArtistResults = null;
                pendingRequests = 3;
                pendingOnlineID = onlineID;
                pendingSequence = currentSequence;

                // Spotlight search
                var spotlightRequest = new SearchBeatmapSetsRequest(
                    query: query,
                    ruleset: ruleset,
                    general: new[] { SearchGeneral.Spotlights },
                    searchCategory: SearchCategory.Any,
                    sortCriteria: SortCriteria.Updated,
                    sortDirection: SortDirection.Descending,
                    genre: genre,
                    language: language,
                    extra: null,
                    ranks: null,
                    played: SearchPlayed.Any,
                    explicitContent: SearchExplicit.Show);

                spotlightRequest.Success += spotlightResponse =>
                {
                    Logger.Log($"[MOsu] Spotlight search success: {spotlightResponse.BeatmapSets.Count()} results", LoggingTarget.Runtime);
                    Schedule(() =>
                    {
                        pendingSpotlightResults = spotlightResponse.BeatmapSets.ToList();
                        if (--pendingRequests == 0 && pendingSequence == currentSequence)
                            populateAllGrids();
                    });
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
                    language: language,
                    extra: null,
                    ranks: null,
                    played: SearchPlayed.Any,
                    explicitContent: SearchExplicit.Show);

                searchRequest.Success += searchResponse =>
                {
                    Logger.Log($"[MOsu] Similar search success: {searchResponse.BeatmapSets.Count()} results", LoggingTarget.Runtime);
                    Schedule(() =>
                    {
                        pendingSuggestionsResults = searchResponse.BeatmapSets.ToList();
                        if (--pendingRequests == 0 && pendingSequence == currentSequence)
                            populateAllGrids();
                    });
                };
                searchRequest.Failure += e => Logger.Log($"[MOsu] Similar search failed: {e}", LoggingTarget.Runtime);
                api.Queue(searchRequest);

                // Same artist search
                var artist = response.Artist;
                string artistStarFilter = "";
                if (minStars > 0)
                    artistStarFilter += $" stars>={minStars}";
                if (!starSlider.UpperBound.IsDefault && maxStars > 0)
                    artistStarFilter += $" stars<={maxStars}";
                var artistQuery = $"artist:\"{artist}\" favourites>1{artistStarFilter}";
                Logger.Log($"[MOsu] Artist search query: {artistQuery}", LoggingTarget.Runtime);

                var artistRequest = new SearchBeatmapSetsRequest(
                    query: artistQuery,
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
                    explicitContent: SearchExplicit.Show);

                artistRequest.Success += artistResponse =>
                {
                    Logger.Log($"[MOsu] Artist search success: {artistResponse.BeatmapSets.Count()} results", LoggingTarget.Runtime);
                    Schedule(() =>
                    {
                        pendingArtistResults = artistResponse.BeatmapSets.ToList();
                        if (--pendingRequests == 0 && pendingSequence == currentSequence)
                            populateAllGrids();
                    });
                };
                artistRequest.Failure += e => Logger.Log($"[MOsu] Artist search failed: {e}", LoggingTarget.Runtime);

                api.Queue(artistRequest);
            };
            getSetRequest.Failure += e => Logger.Log($"[MOsu] GetBeatmapSetRequest failed: {e}", LoggingTarget.Runtime);

            api.Queue(getSetRequest);
        }

        private void populateAllGrids()
        {
            var allResults = new List<(List<APIBeatmapSet> results, ReverseChildIDFillFlowContainer<BeatmapCard> grid)>();
            if (pendingSpotlightResults != null)
                allResults.Add((pendingSpotlightResults, spotlightGrid));
            if (pendingSuggestionsResults != null)
                allResults.Add((pendingSuggestionsResults, suggestionsGrid));
            if (pendingArtistResults != null)
                allResults.Add((pendingArtistResults, artistGrid));

            var allSets = allResults.SelectMany(r => r.results).ToList();
            var onlineIDs = allSets.Where(b => b.OnlineID > 0 && b.OnlineID != pendingOnlineID).Select(b => b.OnlineID).ToList();
            var apiTitlesArtists = allSets.Select(b => (b.Title.ToLowerInvariant(), b.Artist.ToLowerInvariant())).ToList();

            Task.Run(() =>
            {
                var localOnlineIDs = new HashSet<int>();
                var localTitleArtists = new HashSet<(string, string)>();

                if (onlineIDs.Count > 0)
                {
                    var idsStr = string.Join(", ", onlineIDs);
                    localOnlineIDs = realmAccess.Run(r =>
                        r.All<BeatmapSetInfo>()
                         .Filter($"OnlineID IN {{{idsStr}}} AND DeletePending == false")
                         .ToList()
                         .Select(b => b.OnlineID)
                         .ToHashSet());
                }

                if (apiTitlesArtists.Count > 0)
                {
                    var conditions = JoinWithOr(apiTitlesArtists
                        .Select((ta, i) => $"({nameof(BeatmapInfo.Metadata)}.{nameof(BeatmapMetadata.Title)} == $" + (2*i) + $" AND {nameof(BeatmapInfo.Metadata)}.{nameof(BeatmapMetadata.Artist)} == $" + (2*i+1) + ")"));

                    var args = apiTitlesArtists.SelectMany(ta => new[] { ta.Item1, ta.Item2 }).Select(s => (QueryArgument)s).ToArray();

                    localTitleArtists = realmAccess.Run(r =>
                        r.All<BeatmapInfo>()
                         .Filter($"({conditions}) AND {nameof(BeatmapInfo.BeatmapSet)}.{nameof(BeatmapSetInfo.DeletePending)} == false", args)
                         .ToList()
                         .Select(b => (b.Metadata.Title.ToLowerInvariant(), b.Metadata.Artist.ToLowerInvariant()))
                         .Distinct()
                         .ToHashSet());
                }

                Schedule(() =>
                {
                    foreach (var (results, grid) in allResults)
                    {
                        var filtered = results
                            .Where(b => b.OnlineID != pendingOnlineID)
                            .Where(b => b.OnlineID <= 0 || !localOnlineIDs.Contains(b.OnlineID))
                            .Where(b => !localTitleArtists.Contains((b.Title.ToLowerInvariant(), b.Artist.ToLowerInvariant())))
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

                        foreach (var set in finalResults)
                        {
                            var card = new CompactBeatmapCard(set, allowExpansion: true);
                            grid.Add(card);
                        }

                        LoadingLayer loadingIndicator = grid == spotlightGrid ? spotlightLoading : (grid == suggestionsGrid ? suggestionsLoading : artistLoading);
                        loadingIndicator.Hide();
                        grid.FadeIn(300, Easing.OutQuint);
                    }
                });
            });
        }

        private static readonly System.Random random = new System.Random();

        private static string JoinWithOr(IEnumerable<string> conditions)
        {
            return string.Join(" OR ", conditions);
        }

private static readonly HashSet<string> knownGenres = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // pop & abbreviations
            "kpop", "k pop", "jpop", "j pop", "cpop", "c pop", "vpop", "v pop", "tpop", "t pop", "cantopop", "mandopop",
            "dance pop", "electropop", "synthpop", "art pop", "indie pop",
            "dream pop", "hyperpop", "bubblegum pop", "teen pop", "sophisti pop",
            "sunshine pop", "europop", "chamber pop", "space age pop", "baroque pop",
            "power pop", "noise pop", "city pop", "j idol", "shibuya kei",
            "kayokyoku", "enka", "electro pop", "dark pop", "krnb", "k rnb", "kindie", "k indie",

            // vocaloid, virtual singer & internet culture
            "vocaloid", "utau", "synthv", "synthesizer v", "cevio", "neutrino", "vocaloid rock",
            "vocagloss", "vocaloid pop", "denpa", "denpa song", "vtuber", "vtuber music",
            "touhou", "doujin music", "nightcore", "daycore", "sped up", "slowed and reverb",
            "lofi hip hop", "bardcore", "otacore",

            // phonk, hip hop & abbreviations
            "phonk", "drift phonk", "brazilian phonk", "memphis phonk", "rare phonk",
            "wave phonk", "house phonk", "ambient phonk", "dungeon phonk", "metal phonk",
            "jungle phonk", "g phonk", "dirt phonk", "russian phonk", "aggressive phonk",
            "hip hop", "hiphop", "hh", "boom bap", "trap", "cloud rap", "mumble rap", "conscious hip hop",
            "alternative hip hop", "gangsta rap", "g funk", "east coast hip hop",
            "west coast hip hop", "southern hip hop", "dirty south", "crunk",
            "chopped and screwed", "horrorcore", "uk drill", "brooklyn drill",
            "chicago drill", "grime", "plugg", "pluggnb", "rage", "tread rap",
            "hyphy", "snap music", "jerkin", "bounce", "philly rap", "emo rap",
            "industrial hip hop", "jazz rap", "hardcore hip hop", "instrumental hip hop",

            // electronic, club & abbreviations
            "electronic", "house", "deep house", "tech house", "progressive house", "electro house",
            "acid house", "chicago house", "french house", "tropical house", "future house",
            "bass house", "ghetto house", "ghettotech", "slap house", "melodic house",
            "italo house", "tribal house", "techno", "detroit techno", "acid techno",
            "minimal techno", "dub techno", "industrial techno", "hard techno",
            "peak time techno", "melodic techno", "schranz", "dubstep", "brostep",
            "riddim", "tearout", "post dubstep", "chillstep", 
            "drum and bass", "d&b", "dnb", "d n b", "drum & bass", "drum n bass",
            "liquid funk", "neurofunk", "jump up", "techstep", "jungle", "ragga jungle",
            "breakcore", "hardcore breaks", "hardstyle", "rawstyle", "euphoric hardstyle",
            "frenchcore", "gabber", "happy hardcore", "uk hardcore", "ukhc", "speedcore",
            "extratone", "hard dance", "jumpstyle", "makina", "uk garage", "ukg", "2 step",
            "bassline", "future bass", "jersey club", "baltimore club", "philly club",
            "footwork", "juke", "ambient", "dark ambient", "dungeon synth", "drone",
            "lowercase", "psybient", "chillout", "downtempo", "trip hop", "illbient",
            "vaporwave", "future funk", "mallsoft", "hardvapour", "synthwave",
            "darksynth", "chillwave", "sovietwave", "electro", "electroclash",
            "ebm", "futurepop", "aggrotech", "new beat", "chiptune", "bitpop",
            "nintendocore", "trance", "psytrance", "goa trance", "uplifting trance",
            "progressive trance", "tech trance", "vocal trance", "hard trance",
            "edm", "idm", "pbrnb", "lofi", "lo fi",

            // rock, punk & abbreviations
            "rock", "classic rock", "hard rock", "soft rock", "psychedelic rock",
            "garage rock", "surf rock", "space rock", "krautrock", "progressive rock", "prog rock",
            "art rock", "glam rock", "arena rock", "southern rock", "heartland rock",
            "pub rock", "funk rock", "alternative rock", "alt rock", "indie rock", "grunge",
            "post grunge", "shoegaze", "nu gaze", "slowcore", "sadcore", "math rock",
            "post rock", "noise rock", "britpop", "madchester", "j rock", "jrock", "k rock", "krock",
            "punk rock", "punk", "hardcore punk", "post punk", "pop punk", "skate punk",
            "street punk", "anarcho punk", "crust punk", "d beat", "emo",
            "midwest emo", "screamo", "emoviolence", "post hardcore", "phc", "mathcore",
            "grindcore", "goregrind", "pornogrind", "cybergrind", "no wave",
            "gothic rock", "goth rock", "goth", "deathrock", "coldwave", "darkwave", "ethereal wave",

            // metal & abbreviations
            "heavy metal", "thrash metal", "speed metal", "power metal", "death metal", "dm",
            "technical death metal", "tech death", "melodic death metal", "melodeath", "slam death metal", "slam",
            "brutal death metal", "black metal", "bm", "atmospheric black metal", "atmo black",
            "depressive suicidal black metal", "dsbm", "first wave black metal",
            "second wave black metal", "pagan metal", "viking metal", "doom metal",
            "stoner metal", "sludge metal", "sludge", "funeral doom", "epic doom", "gothic metal",
            "symphonic metal", "folk metal", "industrial metal", "nu metal",
            "alternative metal", "metalcore", "melodic metalcore", "deathcore",
            "progressive metal", "prog metal", "djent", "post metal", "neocrust", "drone metal",
            "kawaii metal", "pornocore", "nintendocore metal", "nwobhm",

            // rnb, soul, funk & abbreviations
            "rnb", "r and b", "r & b", "r'n'b", "contemporary rnb", "neo soul", "alternative rnb", "alt rnb", "quiet storm",
            "motown", "northern soul", "southern soul", "deep soul", "psychedelic soul",
            "blue eyed soul", "funk", "p funk", "go go", "boogie", "post disco",
            "disco", "nu disco", "italo disco", "space disco", "euro disco",
            "gospel", "traditional gospel", "urban contemporary gospel", "gospel blues",

            // jazz, blues & abbreviations
            "jazz", "dixieland", "swing", "bebop", "hard bop", "cool jazz",
            "modal jazz", "free jazz", "avant garde jazz", "fusion", "jazz funk",
            "smooth jazz", "acid jazz", "nu jazz", "ethno jazz", "gypsy jazz",
            "post bop", "third stream", "blues", "delta blues", "chicago blues",
            "electric blues", "jump blues", "texas blues", "piedmont blues",
            "country blues", "swamp blues", "blues rock", "acoustic blues",

            // country, folk & abbreviations
            "country", "traditional country", "outlaw country", "country pop",
            "country rock", "honky tonk", "bakersfield sound", "nashville sound",
            "bluegrass", "progressive bluegrass", "newgrass", "americana", "alt country",
            "cowpunk", "red dirt", "bro country", "folk", "traditional folk",
            "contemporary folk", "indie folk", "folk rock", "psychedelic folk",
            "freak folk", "neofolk", "anti folk", "skiffle", "sea shanty",
            "celtic folk", "nordic folk", "eungwe",

            // latin, caribbean & abbreviations
            "reggae", "roots reggae", "dub", "dancehall", "ragga", "ska",
            "two tone", "2 tone", "ska punk", "rocksteady", "reggaeton", "latin trap",
            "salsa", "bachata", "merengue", "cumbia", "cumbia villera",
            "digital cumbia", "samba", "bossa nova", "mpb", "funk carioca",
            "brega funk", "proibidao", "tango", "neotango", "bolero",
            "rumba", "mambo", "son cubano", "tejano", "norteno",
            "ranchera", "mariachi", "corridos", "corridos tumbados", "banda",
            "chicha", "soca", "calypso", "zouk", "kizomba", "kompa",

            // african, middle eastern & abbreviations
            "afrobeats", "afrobeat", "amapiano", "highlife", "hiplife",
            "soukous", "makossa", "juju", "fuji music", "gqom",
            "kwaito", "kuduro", "coupe decale", "singeli", "shangaan electro",
            "mbalax", "bongo flava", "gengetone", "gnawa", "rai",
            "mahraganat", "dabke", "anatolie rock", "arabic pop",

            // classical, experimental & abbreviations
            "classical", "early music", "medieval music", "renaissance music",
            "baroque", "classical period", "romantic period", "modern classical", "neoclassical",
            "minimalism", "serialism", "musique concrete", "electroacoustic",
            "avant garde", "noise", "harsh noise", "harsh noise wall", "hnw", "power electronics",
            "danger music", "plunderphonics", "sound collage", "tape music",
            "field recording", "ambient noise", "musical theatre", "showtunes",
            "film score", "ost", "vgm", "video game music", "production music",

            // regional & niche microgenres
            "polka", "yodeling", "klezmer", "fado", "flamenco",
            "nuevo flamenco", "chanson", "volksmusik", "schlager", "liedermacher",
            "turbofolk", "manele", "chalga", "gagaku", "minyo",
            "protovapor", "bard song", "carnatic", "hindustani", "qawwali",
            "bhangra", "filmi", "indipop", "gamelan", "dangdut",
            "budots", "luk thung", "mor lam", "gothic country",
            "dark cabaret", "steampunk", "zydeco", "cajun", "jug band",
            "witch house", "breakbeat", "breaks", "big beat", "nu skool breaks", "acid breaks",
            "jersey bounce", "drillstep", "dubstyle", "freeform hardcore", "moombahton",
            "moombahcore", "guaracha", "electro funk", "twerk", "deep funk",
            "synth funk", "funktronica", "glitch", "glitch hop", "neurohop",
            "complextro", "fidget house", "melbourne bounce", "dutch house", "speed garage",
            "eurobeat", "spacesynth", "hi nrg", "pumping house", "hardbass",
            "scouse house", "symphonic black metal", "melodic deathcore", "cyber metal", "ndh", "neue deutsche harte",
            "electrogrind", "math metal", "pagan folk", "neoclassical darkwave", "martial industrial",
            "death industrial", "power noise", "hauntology", "hypnagogic pop", "vaporhop",
            "vaportrap", "dreampunk",

            // osu!, rhythm games & Japanese niche
            "jcore", "j-core", "j core", "hi-tech", "hitech", "hitech fullon",
            "artcore", "future core", "kawaii bass", "kawaii future bass", "speed dance",
            "otoge", "bemani", "hard renaissance", "splittercore",
            "halftime", "crossbreed", "gabba", "donk",
            "anisong", "anime ost", "game ost", "doujin", "doujin soft",
            "vocaloid metal", "chaoz", "j-metal", "j metal",
            "melodic speed metal", "technical metal", "visual kei",
            "vke", "speed up"
        };

        private static HashSet<string> extractGenreTags(string tags)
        {
            // Normalize hyphens to spaces, then split
            var normalizedTags = tags.Replace('-', ' ').Replace('_', ' ');
            var tagWords = normalizedTags.Split(new[] { ' ', ',', '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLowerInvariant())
                .ToArray();
            var matches = new HashSet<string>();
            foreach (var genre in knownGenres)
            {
                var genreWords = genre.Split(' ');
                for (int i = 0; i <= tagWords.Length - genreWords.Length; i++)
                {
                    bool match = true;
                    for (int j = 0; j < genreWords.Length; j++)
                    {
                        if (tagWords[i + j] != genreWords[j])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match)
                    {
                        matches.Add(genre);
                        break;
                    }
                }
            }
            Logger.Log($"[MOsu] Matched genres: {string.Join(", ", matches)}", LoggingTarget.Runtime);
            return matches;
        }

        private static SearchGenre toSearchGenre(BeatmapSetOnlineGenre genre)
        {
            if (genre.Id <= 0)
                return SearchGenre.Any;

            return (SearchGenre)genre.Id;
        }

        private static SearchLanguage toSearchLanguage(BeatmapSetOnlineLanguage language)
        {
            if (language.Id <= 0)
                return SearchLanguage.Any;

            return (SearchLanguage)language.Id;
        }
    }
}
