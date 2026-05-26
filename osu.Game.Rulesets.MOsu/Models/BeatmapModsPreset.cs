using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Extensions.ObjectExtensions;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Rulesets;
using Realms;
[assembly: Explicit]

namespace osu.Game.Rulesets.Mods
{
    [Preserve(AllMembers = true)]
    public partial class BeatmapModPreset : RealmObject, IHasGuidPrimaryKey, ISoftDelete
    {
        [PrimaryKey]
        public Guid ID { get; set; } = Guid.NewGuid();

        [Indexed]
        public string BeatmapMD5Hash { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The ruleset that the preset is valid for.
        /// </summary>
        public RulesetInfo Ruleset { get; set; } = null!;

        /// <summary>
        /// The set of configured mods.
        /// </summary>
        [Ignored]
        public ICollection<Mod> Mods
        {
            get
            {
                if (string.IsNullOrEmpty(ModsJson))
                    return Array.Empty<Mod>();

                var apiMods = JsonConvert.DeserializeObject<IEnumerable<APIMod>>(ModsJson);

                // Create the ruleset instance directly from the Realm object
                var ruleset = Ruleset.CreateInstance();
                return apiMods.AsNonNull().Select(mod => mod.ToMod(ruleset)).ToArray();
            }
            set
            {
                if (value == null)
                {
                    ModsJson = string.Empty;
                    return;
                }

                var apiMods = value.Select(mod => new APIMod(mod)).ToArray();
                ModsJson = JsonConvert.SerializeObject(apiMods);
            }
        }

        [MapTo("Mods")]
        public string ModsJson { get; set; } = string.Empty;

        public bool DeletePending { get; set; }
    }
}
