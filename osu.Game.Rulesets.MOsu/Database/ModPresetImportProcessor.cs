using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using osu.Framework.Logging;
using osu.Game.Database;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.MOsu.UI;
using Realms;

namespace osu.Game.Rulesets.MOsu.Database
{
    /// <summary>
    /// Shared mod preset import pipeline: parses preset JSON and writes non-duplicate presets to realm.
    /// The only caller-specific concern is where the JSON comes from.
    /// </summary>
    public class ModPresetImportProcessor
    {
        private readonly RealmAccess realm;
        private readonly INotificationOverlay notifications;
        private readonly Action<Action> schedule;

        public ModPresetImportProcessor(RealmAccess realm, INotificationOverlay notifications, Action<Action> schedule)
        {
            this.realm = realm;
            this.notifications = notifications;
            this.schedule = schedule;
        }

        /// <summary>
        /// Imports mod presets from JSON, deduplicating by name.
        /// <paramref name="onImported"/> fires on the update thread with the number of newly imported presets.
        /// Never throws — errors are posted as notifications.
        /// </summary>
        public void Import(string json, Action<int>? onImported = null)
        {
            try
            {
                var transferObjects = JsonConvert.DeserializeObject<List<ModPresetTransferObject>>(json);

                if (transferObjects == null || transferObjects.Count == 0)
                {
                    schedule(() => notifications.Post(new SimpleErrorNotification { Text = "No presets found in file." }));
                    return;
                }

                int importedCount = 0;

                realm.Write(r =>
                {
                    var rulesetInfo = r.Find<RulesetInfo>(MosuRuleset.SHORT_NAME);
                    if (rulesetInfo == null)
                    {
                        Logger.Log("MOsu ruleset not found in realm, skipping preset import.");
                        return;
                    }

                    foreach (var dto in transferObjects)
                    {
                        bool exists = r.All<ModPreset>()
                            .Filter("Name == $0 && Ruleset.ShortName == $1 && DeletePending == false", dto.Name, MosuRuleset.SHORT_NAME)
                            .Count() > 0;
                        if (exists) continue;

                        r.Add(new ModPreset
                        {
                            ID = Guid.NewGuid(),
                            Name = dto.Name,
                            Description = dto.Description,
                            ModsJson = dto.ModsJson,
                            Ruleset = rulesetInfo,
                            DeletePending = false
                        });
                        importedCount++;
                    }
                });

                schedule(() =>
                {
                    onImported?.Invoke(importedCount);
                    notifications.Post(importedCount > 0
                        ? new SimpleNotification { Text = $"Imported {importedCount} presets." }
                        : new SimpleNotification { Text = "All presets in file were duplicates." });
                });
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to import presets.");
                schedule(() => notifications.Post(new SimpleErrorNotification { Text = $"Failed to import presets: {ex.Message}" }));
            }
        }
    }
}
