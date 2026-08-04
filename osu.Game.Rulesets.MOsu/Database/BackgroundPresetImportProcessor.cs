using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Database;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.MOsu.Configuration;

namespace osu.Game.Rulesets.MOsu.Database
{
    public partial class BackgroundPresetImportProcessor : Component
    {
        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private IRulesetConfigCache configCache { get; set; } = null!;

        [Resolved]
        private INotificationOverlay notifications { get; set; } = null!;

        private MOsuRulesetConfigManager config = null!;

        private const string resource_name = "osu.Game.Rulesets.MOsu.osu_mod_presets.json";

        protected override void LoadComplete()
        {
            base.LoadComplete();

            config = configCache.GetConfigFor(new MosuRuleset()) as MOsuRulesetConfigManager ?? throw new InvalidOperationException("MOsuRulesetConfigManager not found");

            Task.Factory.StartNew(() =>
            {
                try
                {
                    Logger.Log("Beginning MOsu default preset import check..");

                    bool alreadyImported = config.Get<bool>(MOsuRulesetSetting.PresetsImported);

                    if (alreadyImported)
                    {
                        Logger.Log("MOsu default presets already imported, skipping.");
                        return;
                    }

                    string json = readEmbeddedPresets();

                    var processor = new ModPresetImportProcessor(realm, notifications, action => Schedule(action));
                    processor.Import(json);

                    Schedule(() => config.SetValue(MOsuRulesetSetting.PresetsImported, true));
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "Failed to import MOsu default presets.");
                    Schedule(() => notifications.Post(new SimpleErrorNotification
                    {
                        Text = $"Failed to import MOsu presets: {ex.Message}"
                    }));
                }
            }, TaskCreationOptions.LongRunning);
        }

        private static string readEmbeddedPresets()
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
