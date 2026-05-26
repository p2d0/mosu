using System.Collections.Generic;

namespace osu.Game.Rulesets.MOsu.UI {
    public class CollectionTransferObject
    {
        public string Name { get; set; } = string.Empty;
        public List<string> BeatmapMD5Hashes { get; set; } = new List<string>();
    }
}
