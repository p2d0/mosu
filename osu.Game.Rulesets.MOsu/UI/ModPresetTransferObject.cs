using System.Collections.Generic;

namespace osu.Game.Rulesets.MOsu.UI
{
    // A clean class used purely for saving/loading to JSON
    public class ModPresetTransferObject
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ModsJson { get; set; } = string.Empty;
    }
}
