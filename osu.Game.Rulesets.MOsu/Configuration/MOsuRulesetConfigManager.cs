using osu.Game.Configuration;
using osu.Game.Rulesets.Configuration;
using osu.Game.Rulesets.Osu.Configuration;

namespace osu.Game.Rulesets.MOsu.Configuration
{
    public class MOsuRulesetConfigManager : RulesetConfigManager<MOsuRulesetSetting>
    {
        private readonly OsuRulesetConfigManager baseConfig;

        public MOsuRulesetConfigManager(SettingsStore settings, RulesetInfo ruleset, int? variant = null)
            : base(settings, ruleset, variant)
        {
            baseConfig = new OsuRulesetConfigManager(settings, ruleset, variant);
        }

        protected override void InitialiseDefaults()
        {
            base.InitialiseDefaults();
            SetDefault(MOsuRulesetSetting.SuggestedSongsMinStars, 0.0, 0, 10, 0.1);
            SetDefault(MOsuRulesetSetting.SuggestedSongsMaxStars, 10.1, 0, 10.1, 0.1);
        }
    }

    public enum MOsuRulesetSetting
    {
        SuggestedSongsMinStars,
        SuggestedSongsMaxStars,
    }
}
