using System.Collections.Generic;

namespace osu.Game.Rulesets.MOsu.UI {
    public class CollectionTransferObject
    {
        public string Name { get; set; } = string.Empty;
        public List<CollectionBeatmapEntry> Beatmaps { get; set; } = new List<CollectionBeatmapEntry>();
    }

    public class CollectionWithScoresTransferObject
    {
        public string Name { get; set; } = string.Empty;
        public List<CollectionBeatmapEntry> Beatmaps { get; set; } = new List<CollectionBeatmapEntry>();
    }

    public class CollectionBeatmapEntry
    {
        public int BeatmapSetId { get; set; }
        public string BeatmapMD5Hash { get; set; } = string.Empty;
        public List<ScoreExportDto> Scores { get; set; } = new List<ScoreExportDto>();
    }

    public class ScoreExportDto
    {
        public string BeatmapHash { get; set; } = string.Empty;
        public string RulesetShortName { get; set; } = string.Empty;
        public long TotalScore { get; set; }
        public double Accuracy { get; set; }
        public int MaxCombo { get; set; }
        public string Rank { get; set; } = string.Empty;
        public System.DateTimeOffset Date { get; set; }
        public List<osu.Game.Online.API.APIMod> Mods { get; set; } = new List<osu.Game.Online.API.APIMod>();
        public System.Collections.Generic.Dictionary<string, int> Statistics { get; set; } = new System.Collections.Generic.Dictionary<string, int>();
    }
}
