using System.Numerics;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// レンダリング可能な3Dオブジェクト
/// </summary>
public interface I3DRenderableObject
{
    I3DMeshResource Mesh { get; }
    I3DMaterialResource Material { get; }
    Matrix4x4 Transform { get; }
    bool CastShadows { get; }
    bool ReceiveShadows { get; }
}
