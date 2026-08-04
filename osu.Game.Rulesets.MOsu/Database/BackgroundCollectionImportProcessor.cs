using System;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Rulesets.MOsu.Configuration;

namespace osu.Game.Rulesets.MOsu.Database
{
    public partial class BackgroundCollectionImportProcessor : BackgroundEmbeddedImportProcessor
    {
        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        public BackgroundCollectionImportProcessor()
            : base("osu.Game.Rulesets.MOsu.example_collections.json", "collections",
                   c => c.Get<bool>(MOsuRulesetSetting.CollectionsImported),
                   c => c.SetValue(MOsuRulesetSetting.CollectionsImported, true))
        {
        }

        protected override Task Import(string json, Action<Action> schedule)
            => new CollectionImportProcessor(realm, notifications, api, beatmapManager, schedule).Import(json);
    }
}
