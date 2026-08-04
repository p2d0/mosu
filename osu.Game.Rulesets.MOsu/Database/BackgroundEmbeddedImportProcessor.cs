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
    /// <summary>
    /// Imports an embedded JSON resource exactly once per install: reads the resource, hands it to a
    /// downstream import pipeline, and marks the run done in ruleset config — all off the game thread.
    /// The only caller-specific concerns are which resource, which config flag gates it, and which
    /// import pipeline consumes the JSON.
    /// </summary>
    public abstract partial class BackgroundEmbeddedImportProcessor : Component
    {
        [Resolved]
        protected RealmAccess realm { get; private set; } = null!;

        [Resolved]
        private IRulesetConfigCache configCache { get; set; } = null!;

        [Resolved]
        protected INotificationOverlay notifications { get; private set; } = null!;

        private readonly string resourceName;
        private readonly string description;
        private readonly Func<MOsuRulesetConfigManager, bool> isImported;
        private readonly Action<MOsuRulesetConfigManager> markImported;

        private MOsuRulesetConfigManager config = null!;

        protected BackgroundEmbeddedImportProcessor(string resourceName, string description, Func<MOsuRulesetConfigManager, bool> isImported, Action<MOsuRulesetConfigManager> markImported)
        {
            this.resourceName = resourceName;
            this.description = description;
            this.isImported = isImported;
            this.markImported = markImported;
        }

        /// <summary>
        /// Hands the parsed embedded JSON to the caller-specific import pipeline.
        /// </summary>
        /// <param name="json">The embedded resource contents.</param>
        /// <param name="schedule">Invokes an action on the update thread (for notifications).</param>
        protected abstract Task Import(string json, Action<Action> schedule);

        protected override void LoadComplete()
        {
            base.LoadComplete();

            config = configCache.GetConfigFor(new MosuRuleset()) as MOsuRulesetConfigManager ?? throw new InvalidOperationException("MOsuRulesetConfigManager not found");

            Logger.Log($"Beginning MOsu {description} import check..");

            if (isImported(config))
                Logger.Log($"MOsu {description} already imported, skipping.");
            else
                ImportInBackground();
        }

        /// <summary>
        /// Run the import now, regardless of the once-per-install flag. Never throws — errors are posted as notifications.
        /// </summary>
        public void ImportInBackground()
        {
            Task.Factory.StartNew(async () =>
            {
                try
                {
                    string json = readEmbeddedResource(resourceName);
                    await Import(json, action => Schedule(action));
                    Schedule(() => markImported(config));
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, $"Failed to import MOsu {description}.");
                    Schedule(() => notifications.Post(new SimpleErrorNotification
                    {
                        Text = $"Failed to import MOsu {description}: {ex.Message}"
                    }));
                }
            }, TaskCreationOptions.LongRunning);
        }

        private static string readEmbeddedResource(string resourceName)
        {
            var assembly = Assembly.GetExecutingAssembly();

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new FileNotFoundException($"Embedded resource '{resourceName}' not found.");

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
