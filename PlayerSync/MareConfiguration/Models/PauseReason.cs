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

public static class PauseReasonText
{
    public static string FromPauseReason(PauseReason reason)
    {
        return reason switch
        {
            PauseReason.None => "Unknown",
            PauseReason.Manual => "Manually paused by user",
            PauseReason.Permanent => "Permanently paused by user",
            PauseReason.ThresholdVram => "Exceeded VRAM threshold",
            PauseReason.ThresholdTriangles => "Exceeded triangles threshold",
            PauseReason.ThresholdHeight => "Exceeded height threshold",
            PauseReason.PauseSyncshell => "In a paused syncshell",
            PauseReason.PauseAllPairs => "User paused all pairs",
            PauseReason.PauseAllSyncs => "User paused all syncshells",
            _ => string.Empty
        };
    }
}