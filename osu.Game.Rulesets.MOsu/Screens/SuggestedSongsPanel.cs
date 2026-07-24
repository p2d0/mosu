using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using osu.Framework.Allocation;
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
        private ReverseChildIDFillFlowContainer<BeatmapCard> artistGrid;
        private Container spotlightLoading;
        private Container suggestionsLoading;
        private Container artistLoading;
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
                        spotlightLoading = new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 100,
                            Children = new Drawable[]
                            {
                                new LoadingSpinner
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                },
                            },
                        },
                        spotlightGrid = new ReverseChildIDFillFlowContainer<BeatmapCard>
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Full,
                            Spacing = new Vector2(10),
                            Alpha = 0,
                        },
                        new OsuSpriteText
                        {
                            Text = "Similar Beats",
                            Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                            Colour = OsuColour.Gray(0.7f),
                        },
                        suggestionsLoading = new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 100,
                            Children = new Drawable[]
                            {
                                new LoadingSpinner
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                },
                            },
                        },
                        suggestionsGrid = new ReverseChildIDFillFlowContainer<BeatmapCard>
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Full,
                            Spacing = new Vector2(10),
                            Alpha = 0,
                        },
                        new OsuSpriteText
                        {
                            Text = "Same Artist",
                            Font = OsuFont.GetFont(size: 14, weight: FontWeight.SemiBold),
                            Colour = OsuColour.Gray(0.7f),
                        },
                        artistLoading = new Container
                        {
                            RelativeSizeAxes = Axes.X,
                            Height = 100,
                            Children = new Drawable[]
                            {
                                new LoadingSpinner
                                {
                                    Anchor = Anchor.Centre,
                                    Origin = Anchor.Centre,
                                },
                            },
                        },
                        artistGrid = new ReverseChildIDFillFlowContainer<BeatmapCard>
                        {
                            RelativeSizeAxes = Axes.X,
                            AutoSizeAxes = Axes.Y,
                            Direction = FillDirection.Full,
                            Spacing = new Vector2(10),
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
                var language = toSearchLanguage(response.Language);
                double bpm = response.Beatmaps.Max(b => b.BPM);
                double minBpm = bpm - 10;
                double maxBpm = bpm + 10;

                // Extract genre tags from local beatmap
                var localBeatmap = score.BeatmapInfo.BeatmapSet.Beatmaps.FirstOrDefault();
                var rawTags = localBeatmap?.Metadata.Tags ?? "";
                Logger.Log($"[MOsu] Raw tags: {rawTags}", LoggingTarget.Runtime);
                var genreTags = extractGenreTags(rawTags);
                Logger.Log($"[MOsu] Matched genre tags: {string.Join(", ", genreTags)} (count={genreTags.Count})", LoggingTarget.Runtime);
                var genreQuery = genreTags.Count > 0 ? " " + string.Join(" ", genreTags.Select(t => $"\"{t}\"")) : "";

                var query = $"favourites>1 bpm>={minBpm} bpm<={maxBpm}{genreQuery}";
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
                    language: language,
                    extra: null,
                    ranks: null,
                    played: SearchPlayed.Any,
                    explicitContent: SearchExplicit.Show);

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
                    language: language,
                    extra: null,
                    ranks: null,
                    played: SearchPlayed.Any,
                    explicitContent: SearchExplicit.Show);

                searchRequest.Success += searchResponse =>
                {
                    Logger.Log($"[MOsu] Similar search success: {searchResponse.BeatmapSets.Count()} results", LoggingTarget.Runtime);
                    Schedule(() => populateGrid(suggestionsGrid, searchResponse.BeatmapSets, onlineID));
                };
                searchRequest.Failure += e => Logger.Log($"[MOsu] Similar search failed: {e}", LoggingTarget.Runtime);
                api.Queue(searchRequest);

                // Same artist search
                var artist = response.Artist;
                var artistQuery = $"artist:\"{artist}\" favourites>1";
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
                    Schedule(() => populateGrid(artistGrid, artistResponse.BeatmapSets, onlineID));
                };
                artistRequest.Failure += e => Logger.Log($"[MOsu] Artist search failed: {e}", LoggingTarget.Runtime);

                api.Queue(artistRequest);
            };
            getSetRequest.Failure += e => Logger.Log($"[MOsu] GetBeatmapSetRequest failed: {e}", LoggingTarget.Runtime);

            api.Queue(getSetRequest);
        }

        private static readonly System.Random random = new System.Random();

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

        private void populateGrid(ReverseChildIDFillFlowContainer<BeatmapCard> grid, IEnumerable<APIBeatmapSet> beatmapSets, int excludeOnlineID)
        {
            var beatmapSetsList = beatmapSets.ToList();

            Task.Run(() =>
            {
                var localSets = beatmapManager.GetAllUsableBeatmapSets();
                var downloadedIDs = localSets.Where(b => b.OnlineID > 0).Select(b => b.OnlineID).ToHashSet();
                var localTitles = localSets.Select(b => (b.Metadata.Title.ToLowerInvariant(), b.Metadata.Artist.ToLowerInvariant())).ToHashSet();

                var filtered = beatmapSetsList
                    .Where(b => b.OnlineID != excludeOnlineID)
                    .Where(b => !downloadedIDs.Contains(b.OnlineID))
                    .Where(b => !localTitles.Contains((b.Title.ToLowerInvariant(), b.Artist.ToLowerInvariant())))
                    .ToList();

                // Randomize if more than 9 results
                if (filtered.Count > 12)
                {
                    for (int i = filtered.Count - 1; i > 0; i--)
                    {
                        int j = random.Next(i + 1);
                        (filtered[i], filtered[j]) = (filtered[j], filtered[i]);
                    }
                }

                var results = filtered.Take(12).ToList();
                Logger.Log($"[MOsu] populateGrid: {filtered.Count} filtered, showing {results.Count}", LoggingTarget.Runtime);

                Schedule(() =>
                {
                    foreach (var set in results)
                    {
                        var card = BeatmapCard.Create(set, BeatmapCardSize.Normal, allowExpansion: true);
                        grid.Add(card);
                    }

                    // Hide loading, show grid
                    Container loadingIndicator = grid == spotlightGrid ? spotlightLoading : (grid == suggestionsGrid ? suggestionsLoading : artistLoading);
                    loadingIndicator.FadeOut(200, Easing.OutQuint);
                    grid.FadeIn(300, Easing.OutQuint);
                });
            });
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
