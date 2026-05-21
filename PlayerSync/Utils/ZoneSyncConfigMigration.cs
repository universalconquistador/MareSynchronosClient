using MareSynchronos.MareConfiguration.Configurations;
using MareSynchronos.MareConfiguration.Models;

namespace PlayerSync.Utils;

public static class ZoneSyncConfigMigration
{
    /// <summary>
    /// Migrates ZoneSyncConfig to the latest version.
    /// Returns true if migration was applied and the config should be saved.
    /// </summary>
    public static bool Migrate(ZoneSyncConfig config)
    {
        if (config.Version >= 2) return false;

        switch (config.ZoneSyncFilter)
        {
            case ZoneSyncFilter.All:
                config.EnableFieldSync = true;
                config.EnableResidentialSync = true;
                config.EnableTownSync = true;
                config.EnableDungeonSync = true;
                config.EnablePvpSync = true;
                break;
            case ZoneSyncFilter.ResidentialOnly:
                config.EnableFieldSync = false;
                config.EnableResidentialSync = true;
                config.EnableTownSync = false;
                config.EnableDungeonSync = false;
                config.EnablePvpSync = false;
                break;
            case ZoneSyncFilter.TownOnly:
                config.EnableFieldSync = false;
                config.EnableResidentialSync = false;
                config.EnableTownSync = true;
                config.EnableDungeonSync = false;
                config.EnablePvpSync = false;
                break;
            case ZoneSyncFilter.ResidentialTown:
                config.EnableFieldSync = false;
                config.EnableResidentialSync = true;
                config.EnableTownSync = true;
                config.EnableDungeonSync = false;
                config.EnablePvpSync = false;
                break;
        }

        config.Version = 2;
        return true;
    }
}
