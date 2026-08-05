using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Overlays.BeatmapListing;

namespace osu.Game.Rulesets.MOsu.Utils
{
    /// <summary>
    /// Pure search-query construction for the suggested-songs panel: maps beatmap tags to genre
    /// keywords and builds osu!web search query strings. No game-host dependencies — testable headless.
    /// </summary>
    public static class SuggestionQueryBuilder
    {
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

    public static HashSet<string> ExtractGenreTags(string tags)
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

    public static SearchGenre ToSearchGenre(BeatmapSetOnlineGenre genre)
    {
        if (genre.Id <= 0)
            return SearchGenre.Any;

        return (SearchGenre)genre.Id;
    }

    public static SearchLanguage ToSearchLanguage(BeatmapSetOnlineLanguage language)
    {
        if (language.Id <= 0)
            return SearchLanguage.Any;

        return (SearchLanguage)language.Id;
    }
        /// <summary>
        /// Builds the main "similar beats" query: bpm window, star range, then any matched genre tags.
        /// <paramref name="maxStarsIsDefault"/> mirrors the config bindable's <c>IsDefault</c> — an untouched
        /// upper bound means "no cap" and omits the <c>stars&lt;=</c> clause.
        /// </summary>
        public static string BuildMainQuery(double minBpm, double maxBpm, double minStars, double maxStars, bool maxStarsIsDefault, string tags)
        {
            string starFilter = "";
            if (minStars > 0) starFilter += $" stars>={minStars}";
            if (!maxStarsIsDefault && maxStars > 0) starFilter += $" stars<={maxStars}";

            var genreTags = ExtractGenreTags(tags);
            var genreQuery = genreTags.Count > 0 ? " " + string.Join(" ", genreTags.Select(t => $"\"{t}\"")) : "";

            return $"favourites>1 bpm>={minBpm} bpm<={maxBpm}{starFilter}{genreQuery}";
        }

        /// <summary>
        /// Builds the "same artist" query: artist term plus the same star-range clauses.
        /// </summary>
        public static string BuildArtistQuery(string artist, double minStars, double maxStars, bool maxStarsIsDefault)
        {
            string starFilter = "";
            if (minStars > 0) starFilter += $" stars>={minStars}";
            if (!maxStarsIsDefault && maxStars > 0) starFilter += $" stars<={maxStars}";

            return $"artist:\"{artist}\" favourites>1{starFilter}";
        }
    }
}
