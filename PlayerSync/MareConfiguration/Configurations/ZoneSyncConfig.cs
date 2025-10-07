using MareSynchronos.MareConfiguration.Models;

namespace MareSynchronos.MareConfiguration.Configurations;

public class ZoneSyncConfig : IMareConfiguration
{
    public int Version { get; set; } = 1;
    public bool EnableGroupZoneSyncJoining { get; set; } = false;
    public bool UserHasConfirmedWarning { get; set; } = false;
    public ZoneSyncFilter ZoneSyncFilter { get; set; } = ZoneSyncFilter.All;
    public bool DisableSounds { get; set; } = true;
    public bool DisableVFX { get; set; } = true;
    public bool DisableAnimations { get; set; } = true;
}