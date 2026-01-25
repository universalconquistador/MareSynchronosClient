using System.Numerics;
using Dalamud.Interface.Utility;

namespace MareSynchronos.UI.ModernUi;

/// <summary>
/// Scaling helpers using Dalamud's GlobalScale
/// </summary>
public static class UiScale
{
    /// <summary>
    /// Gets Dalamud global scale, 0.8–3.0
    /// </summary>
    public static float Global => ImGuiHelpers.GlobalScale;

    /// <summary>Scale a scalar value by <see cref="Global"/>.</summary>
    public static float S(float px) => px * Global;

    /// <summary>Scale a Vector2 by <see cref="Global"/>.</summary>
    public static Vector2 V(float x, float y) => new(S(x), S(y));

    /// <summary>Scale a Vector2 by <see cref="Global"/>.</summary>
    public static Vector2 V(Vector2 px) => px * Global;
}
