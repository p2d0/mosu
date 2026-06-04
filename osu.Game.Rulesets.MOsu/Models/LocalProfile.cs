using Realms;

namespace osu.Game.Rulesets.MOsu.Models
{
    public class LocalProfile : RealmObject
    {
        [PrimaryKey]
        public string Name { get; set; } = "";
    }
}
