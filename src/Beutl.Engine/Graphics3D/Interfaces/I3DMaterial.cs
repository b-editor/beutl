using System.Numerics;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dマテリアルインターフェース
/// </summary>
public interface I3DMaterial
{
    Vector3 Albedo { get; }
    float Metallic { get; }
    float Roughness { get; }
    Vector3 Emission { get; }
    ITexture? AlbedoTexture { get; }
    ITexture? NormalTexture { get; }
    ITexture? MetallicRoughnessTexture { get; }
}
