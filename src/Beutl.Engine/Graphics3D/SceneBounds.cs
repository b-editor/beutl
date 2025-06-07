using System.Numerics;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// シーンバウンディング情報
/// </summary>
public struct SceneBounds
{
    public Vector3 Min;
    public Vector3 Max;
    public Vector3 Center;
    public Vector3 Size => Max - Min;
}
