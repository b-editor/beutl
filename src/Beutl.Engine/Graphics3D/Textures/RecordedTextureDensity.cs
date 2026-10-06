using Beutl.Graphics;
using Beutl.Graphics.Rendering;

namespace Beutl.Graphics3D.Textures;

/// <summary>The density a recorded texture source rasterizes its content at.</summary>
internal static class RecordedTextureDensity
{
    /// <summary>
    /// Sanitizes <paramref name="density"/>, then clamps it to what an allocation covering
    /// <paramref name="textureDomain"/> can reach.
    /// </summary>
    public static float Resolve(Rect textureDomain, float density)
    {
        float sanitizedDensity = RenderScaleUtilities.SanitizeOutputScale(density);
        return BufferDimensionBudget.Resolve(BufferBudgetScope.Allocation).ClampWorkingScale(
            textureDomain,
            sanitizedDensity);
    }
}
