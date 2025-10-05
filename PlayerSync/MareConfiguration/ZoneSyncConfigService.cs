using MareSynchronos.MareConfiguration.Configurations;

namespace MareSynchronos.MareConfiguration;

public class ZoneSyncConfigService : ConfigurationServiceBase<ZoneSyncConfig>
{
    public const string ConfigName = "zonesync.json";
    public ZoneSyncConfigService(string configDir) : base(configDir) { }

    public override string ConfigurationName => ConfigName;
}