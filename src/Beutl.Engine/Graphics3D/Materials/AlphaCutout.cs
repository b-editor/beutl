using System.Numerics;
using Beutl.Graphics3D.Textures;

namespace Beutl.Graphics3D.Materials;

/// <summary>The color map whose alpha decides, texel by texel, which parts of a surface exist.</summary>
/// <param name="Map">The color map.</param>
/// <param name="AlphaScale">What the map's alpha is multiplied by, from the material's color and opacity.</param>
/// <param name="Cutoff">The alpha a texel needs to be kept.</param>
internal readonly record struct AlphaCutout(TextureSource.Resource Map, float AlphaScale, float Cutoff)
{
    /// <summary>A map without pixels at hand keeps its whole surface.</summary>
    public bool IsCutOutAt(Vector2 texCoord)
        => Map.TryGetAlpha(texCoord, out float alpha) && AlphaScale * alpha < Cutoff;

    internal Vector4 ShaderParameters => new(AlphaScale, Cutoff, 0, 0);
}
