using System;
using Realms;
using osu.Game.Database;

namespace osu.Game.Rulesets.MOsu.Models
{
    [Preserve(AllMembers = true)]
    public partial class PresetImportState : RealmObject, IHasGuidPrimaryKey
    {
        [PrimaryKey]
        public Guid ID { get; set; } = Guid.NewGuid();

        public bool Imported { get; set; }
        public bool CollectionsImported { get; set; }
    }
}
