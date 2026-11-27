namespace Beutl.Models;

/// <summary>
/// Global working-scale ceiling (MaxWorkingScale) for preview and export render requests.
/// Caps the supply-driven working scale on the high side only.
/// </summary>
public static class WorkingScaleCeiling
{
    /// <summary>Preview ceiling: <c>2 * outputScale</c>.</summary>
    public static float Preview(float outputScale) => 2f * outputScale;

    /// <summary>Export ceiling: none (positive infinity). Allocation enforces per-buffer dimensions and total live bytes.</summary>
    public static float Export() => float.PositiveInfinity;
}
