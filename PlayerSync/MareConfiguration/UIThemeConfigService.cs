using MareSynchronos.MareConfiguration.Configurations;

namespace MareSynchronos.MareConfiguration;

public class UIThemeConfigService : ConfigurationServiceBase<UIThemeConfig>
{
    public const string ConfigName = "uitheme.json";
    public UIThemeConfigService(string configDir) : base(configDir) { }

    public override string ConfigurationName => ConfigName;
}