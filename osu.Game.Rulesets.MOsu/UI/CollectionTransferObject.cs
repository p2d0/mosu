using System.Collections.Generic;
using System.Linq;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.MOsu.UI
{
    public class CollectionTransferObject
    {
        public string Name { get; set; } = string.Empty;
        public List<CollectionBeatmapEntry> Beatmaps { get; set; } = new List<CollectionBeatmapEntry>();
    }

    public class CollectionBeatmapEntry
    {
        public int BeatmapSetId { get; set; }
        public string BeatmapMD5Hash { get; set; } = string.Empty;
        public string BeatmapTitle { get; set; } = string.Empty;
        public string BeatmapAuthor { get; set; } = string.Empty;
        public List<ScoreExportDto> Scores { get; set; } = new List<ScoreExportDto>();
    }

    public class ScoreExportDto
    {
        public string BeatmapHash { get; set; } = string.Empty;
        public string RulesetShortName { get; set; } = string.Empty;
        public string BeatmapDifficultyName { get; set; } = string.Empty;
        public long TotalScore { get; set; }
        public double Accuracy { get; set; }
        public int MaxCombo { get; set; }
        public string Rank { get; set; } = string.Empty;
        public System.DateTimeOffset Date { get; set; }
        public List<osu.Game.Online.API.APIMod> Mods { get; set; } = new List<osu.Game.Online.API.APIMod>();
        public System.Collections.Generic.Dictionary<string, int> Statistics { get; set; } = new System.Collections.Generic.Dictionary<string, int>();
        public string CustomName { get; set; } = string.Empty;

        /// <summary>
        /// Online id of the player who set the score.
        /// </summary>
        public int UserOnlineId { get; set; }

        /// <summary>
        /// Username of the player who set the score.
        /// </summary>
        public string UserUsername { get; set; } = string.Empty;

        /// <summary>
        /// Builds a transfer object from a realm score, including the player's online id and username.
        /// </summary>
        public static ScoreExportDto FromScore(ScoreInfo score)
        {
            return new ScoreExportDto
            {
                BeatmapHash = score.BeatmapInfo!.MD5Hash,
                RulesetShortName = score.Ruleset.ShortName,
                BeatmapDifficultyName = score.BeatmapInfo.DifficultyName,
                TotalScore = score.TotalScore,
                Accuracy = score.Accuracy,
                MaxCombo = score.MaxCombo,
                Rank = score.Rank.ToString(),
                Date = score.Date,
                Mods = score.Mods.Select(m => new osu.Game.Online.API.APIMod(m)).ToList(),
                Statistics = score.Statistics.ToDictionary(k => k.Key.ToString(), v => v.Value),
                UserOnlineId = score.User.OnlineID,
                UserUsername = score.User.Username,
            };
        }
    }
}
