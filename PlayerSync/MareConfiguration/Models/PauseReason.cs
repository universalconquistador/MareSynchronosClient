namespace MareSynchronos.MareConfiguration.Models;

public enum PauseReason
{
    None,
    Manual,
    Permanent,
    ThresholdVram,
    ThresholdTriangles,
    ThresholdHeight,
    PauseSyncshell,
    PauseAllPairs,
    PauseAllSyncs
}