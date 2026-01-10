namespace MareSynchronos.MareConfiguration.Configurations;

/// <summary>
/// The policy for when to use server-compressed alternate files when available.
/// </summary>
public enum CompressedAlternateUsage
{
    /// <summary>
    /// Never use the server-compressed alternate for a file.
    /// </summary>
    AlwaysSourceQuality = 0,

    /// <summary>
    /// Download and use the server-compressed alternates for files that
    /// have not been downloaded on the local PC yet.
    /// </summary>
    /// <remarks>
    /// This improves download speed, storage, and bandwidth savings, but won't
    /// give VRAM savings for files that are already downloaded.
    /// </remarks>
    CompressedNewDownloads = 1,

    /// <summary>
    /// Always download and use the server-compressed alternates for files,
    /// even if the source quality is already present on the local PC.
    /// </summary>
    /// <remarks>
    /// This means VRAM savings on top of the download speed/bandwidth savings
    /// the other compression modes get.
    /// </remarks>
    AlwaysCompressed = 3,
}

public class PlayerPerformanceConfig : IMareConfiguration
{
    // TODO: When we are confident in this feature, change the default
    public const CompressedAlternateUsage DefaultTextureCompressionMode = CompressedAlternateUsage.AlwaysCompressed;

    public int Version { get; set; } = 1;
    public CompressedAlternateUsage? TextureCompressionMode { get; set; } = null;
    public bool ShowPerformanceIndicator { get; set; } = true;
    public bool WarnOnExceedingThresholds { get; set; } = true;
    public bool WarnOnPreferredPermissionsExceedingThresholds { get; set; } = false;
    public int VRAMSizeWarningThresholdMiB { get; set; } = 375;
    public int TrisWarningThresholdThousands { get; set; } = 165;
    public bool AutoPausePlayersExceedingThresholds { get; set; } = false;
    public bool AutoPausePlayersWithPreferredPermissionsExceedingThresholds { get; set; } = false;
    public int VRAMSizeAutoPauseThresholdMiB { get; set; } = 550;
    public int TrisAutoPauseThresholdThousands { get; set; } = 250;
    public List<string> UIDsToIgnore { get; set; } = new();
    public bool AutoPausePlayersExceedingHeightThresholds { get; set; } = false;
    public bool NoAutoPauseDirectPairs { get; set; } = true;
    public bool WarnOnAutoHeightExceedingThreshold {  get; set; } = false;
    public float MaxHeightMultiplier { get; set; } = 200f;
    public bool MaxHeightManual { get; set; } = false;
    public int MaxHeightAbsolute { get; set; } = 366;
    public List<string> UIDsToIgnoreForHeightPausing { get; set; } = new();
    public List<string> UIDsToOverride { get; set; } = new();
}

public static class PlayerPerformanceConfigExtensions
{
    extension(PlayerPerformanceConfig perfConfig)
    {
        public CompressedAlternateUsage TextureCompressionModeOrDefault => perfConfig.TextureCompressionMode ?? PlayerPerformanceConfig.DefaultTextureCompressionMode;
    }
}
