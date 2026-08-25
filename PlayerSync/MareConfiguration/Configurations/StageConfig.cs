using MareSynchronos.MareConfiguration.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace MareSynchronos.MareConfiguration.Configurations;

public class StageConfig : IMareConfiguration
{
    public int Version { get; set; } = 1;

    public bool EnableStageFeatures { get; set; } = false; // TODO: Set to TRUE by default when the feature is officially released
    public CompressedAlternateUsage StageModCompressionUsage { get; set; } = CompressedAlternateUsage.AlwaysCompressed;
    public NotificationLocation StageSavedNotificationLocation { get; set; } = NotificationLocation.Both;

    public HashSet<string> HiddenStageIds { get; set; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> StageIdUploadedDefinitionPath { get; set; } = new(StringComparer.Ordinal);
}
