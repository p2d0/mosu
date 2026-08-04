using System;
using System.Threading.Tasks;
using osu.Game.Rulesets.MOsu.Configuration;

namespace osu.Game.Rulesets.MOsu.Database
{
    public partial class BackgroundPresetImportProcessor : BackgroundEmbeddedImportProcessor
    {
        public BackgroundPresetImportProcessor()
            : base("osu.Game.Rulesets.MOsu.osu_mod_presets.json", "presets",
                   c => c.Get<bool>(MOsuRulesetSetting.PresetsImported),
                   c => c.SetValue(MOsuRulesetSetting.PresetsImported, true))
        {
        }

        protected override Task Import(string json, Action<Action> schedule)
        {
            new ModPresetImportProcessor(realm, notifications, schedule).Import(json);
            return Task.CompletedTask;
        }
    }
}
