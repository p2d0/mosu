using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Database;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.MOsu.Models;
using osu.Game.Rulesets.MOsu.UI;
using Realms;

namespace osu.Game.Rulesets.MOsu.Database
{
    public partial class BackgroundPresetImportProcessor : Component
    {
        [Resolved]
        private MOsuRealmAccess mosuRealm { get; set; } = null!;

        [Resolved]
        private RealmAccess realm { get; set; } = null!;

        [Resolved]
        private INotificationOverlay notifications { get; set; } = null!;

        private const string resource_name = "osu.Game.Rulesets.MOsu.osu_mod_presets.json";

        protected override void LoadComplete()
        {
            base.LoadComplete();

            Task.Factory.StartNew(() =>
            {
                try
                {
                    Logger.Log("Beginning MOsu default preset import check..");

                    bool alreadyImported = mosuRealm.Run(r =>
                    {
                        var state = r.All<PresetImportState>().FirstOrDefault();
                        return state?.Imported ?? false;
                    });

                    if (alreadyImported)
                    {
                        Logger.Log("MOsu default presets already imported, skipping.");
                        return;
                    }

                    string json = readEmbeddedPresets();

                    var transferObjects = JsonConvert.DeserializeObject<List<ModPresetTransferObject>>(json);

                    if (transferObjects == null || transferObjects.Count == 0)
                    {
                        Logger.Log("No embedded presets found to import.");
                        return;
                    }

                    int importedCount = 0;

                    realm.Write(r =>
                    {
                        var osuRulesetInfo = r.Find<RulesetInfo>("mosu");
                        if (osuRulesetInfo == null)
                        {
                            Logger.Log("MOsu ruleset not found in realm, skipping preset import.");
                            return;
                        }

                        foreach (var dto in transferObjects)
                        {
                            bool exists = r.All<ModPreset>()
                                .Filter("Name == $0 && Ruleset.ShortName == $1 && DeletePending == false", dto.Name, "mosu")
                                .Count() > 0;

                            if (exists) continue;

                            r.Add(new ModPreset
                            {
                                ID = Guid.NewGuid(),
                                Name = dto.Name,
                                Description = dto.Description,
                                ModsJson = dto.ModsJson,
                                Ruleset = osuRulesetInfo,
                                DeletePending = false
                            });
                            importedCount++;
                        }
                    });

                    Logger.Log($"Imported {importedCount} MOsu default presets.");

                    mosuRealm.Write(r =>
                    {
                        var state = r.All<PresetImportState>().FirstOrDefault();
                        if (state == null)
                        {
                            r.Add(new PresetImportState { Imported = true });
                        }
                        else
                        {
                            state.Imported = true;
                        }
                    });

                    Schedule(() => notifications.Post(new SimpleNotification
                    {
                        Text = $"MOsu example mod presets imported! ({importedCount} presets)"
                    }));
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
