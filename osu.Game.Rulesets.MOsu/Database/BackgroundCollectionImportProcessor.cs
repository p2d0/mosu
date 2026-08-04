using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Online.API;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.MOsu.Configuration;

namespace osu.Game.Rulesets.MOsu.Database
{
    public partial class BackgroundCollectionImportProcessor : Component
    {
        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private IRulesetConfigCache configCache { get; set; } = null!;

        [Resolved]
        private INotificationOverlay notifications { get; set; } = null!;

        [Resolved]
        private IAPIProvider api { get; set; } = null!;

        [Resolved]
        private BeatmapManager beatmapManager { get; set; } = null!;

        private const string resource_name = "osu.Game.Rulesets.MOsu.example_collections.json";

        private MOsuRulesetConfigManager config = null!;

        protected override void LoadComplete()
        {
            base.LoadComplete();

            config = configCache.GetConfigFor(new MosuRuleset()) as MOsuRulesetConfigManager ?? throw new InvalidOperationException("MOsuRulesetConfigManager not found");

            Logger.Log("Beginning MOsu default collection import check..");

            bool alreadyImported = config.Get<bool>(MOsuRulesetSetting.CollectionsImported);

            if (!alreadyImported)
                ImportExampleCollections();
            else
                Logger.Log("MOsu default collections already imported, skipping.");
        }

        public void ImportExampleCollections()
        {
            Task.Factory.StartNew(async () =>
            {
                try
                {
                    string json = readEmbeddedCollections();
                    var processor = new CollectionImportProcessor(realm, notifications, api, beatmapManager, action => Schedule(action));
                    await processor.Import(json, importScores: true);
                    markCollectionsImported();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to import MOsu default collections.");
                    Schedule(() => notifications.Post(new SimpleErrorNotification
                    {
                        Text = $"Failed to import MOsu collections: {ex.Message}"
                    }));
                }
            }, TaskCreationOptions.LongRunning);
        }

        private void markCollectionsImported()
        {
            Schedule(() => config.SetValue(MOsuRulesetSetting.CollectionsImported, true));
        }

        private static string readEmbeddedCollections()
        {
            var assembly = Assembly.GetExecutingAssembly();

            using (var stream = assembly.GetManifestResourceStream(resource_name))
            {
                if (stream == null)
                    throw new FileNotFoundException($"Embedded resource '{resource_name}' not found.");

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
