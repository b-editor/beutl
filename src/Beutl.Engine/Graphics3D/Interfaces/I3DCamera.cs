using System.Numerics;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dカメラインターフェース
/// </summary>
public interface I3DCamera
{
    Vector3 Position { get; }
    Vector3 Target { get; }
    Vector3 Up { get; }
    float FieldOfView { get; }
    float AspectRatio { get; }
    float NearClip { get; }
    float FarClip { get; }
    Matrix4x4 ViewMatrix { get; }
    Matrix4x4 ProjectionMatrix { get; }
}
