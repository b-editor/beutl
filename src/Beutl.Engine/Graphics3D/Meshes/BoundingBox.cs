using System.Numerics;

namespace Beutl.Graphics3D.Meshes;

/// <summary>
/// Represents an axis-aligned bounding box.
/// </summary>
public readonly struct BoundingBox
{
    public BoundingBox(Vector3 min, Vector3 max)
    {
        Min = min;
        Max = max;
    }

    public Vector3 Min { get; }
    public Vector3 Max { get; }

    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => Max - Min;
}
