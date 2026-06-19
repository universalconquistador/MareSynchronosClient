namespace MareSynchronos.Services.Models;

// https://dalamud.dev/api/Dalamud.Plugin.Services/Interfaces/ITargetManager

/// <summary>
/// Target types as defined by the Dalamud ITargetManager.
/// Note: These are not the same as the ClientStructs TargetType enum.
/// </summary>
public enum TargetType
{
    Target,
    MouseOverTarget,
    FocusTarget,
    PreviousTarget,
    SoftTarget,
    GPoseTarget,
    MouseOverNameplateTarget
}