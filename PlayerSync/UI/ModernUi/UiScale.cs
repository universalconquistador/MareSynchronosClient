// ModernUi/UiScale.cs
using System;
using System.Numerics;
using Dalamud.Interface.Utility;

namespace MareSynchronos.UI.ModernUi;

/// <summary>
/// Centralized scaling helpers.
/// Use these for any “pixel-ish” constants so the entire UI scales with Dalamud.
/// </summary>
public static class UiScale
{
    /// <summary>
    /// Gets Dalamud global scale (user-controlled, usually 0.8–3.0).
    /// </summary>
    public static float Global => ImGuiHelpers.GlobalScale;

    /// <summary>Scale a scalar value by <see cref="Global"/>.</summary>
    public static float S(float px) => px * Global;

    /// <summary>Scale a Vector2 by <see cref="Global"/>.</summary>
    public static Vector2 V(float x, float y) => new(S(x), S(y));

    /// <summary>Scale a Vector2 by <see cref="Global"/>.</summary>
    public static Vector2 V(Vector2 px) => px * Global;

    /// <summary>
    /// Use when you want a “line height-ish” spacing that tracks the current font size.
    /// </summary>
    public static float Em(float ems) => ems * Dalamud.Bindings.ImGui.ImGui.GetFontSize();

    /// <summary>
    /// Clamp a size to a min/max range expressed in “unscaled pixels”.
    /// </summary>
    public static float Clamp(float px, float minPx, float maxPx)
        => Math.Clamp(px, minPx, maxPx) * Global;
}
