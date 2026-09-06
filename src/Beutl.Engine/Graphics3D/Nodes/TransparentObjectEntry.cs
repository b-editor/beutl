using System.Numerics;

namespace Beutl.Graphics3D.Nodes;

/// <summary>
/// Entry for a transparent object to be rendered.
/// </summary>
public struct TransparentObjectEntry
{
    public Object3D.Resource Object;
    public Matrix4x4 WorldMatrix;
    public float DistanceToCamera;
}
