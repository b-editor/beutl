using System.Numerics;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 3Dレイ
/// </summary>
public struct Ray
{
    /// <summary>
    /// 原点
    /// </summary>
    public Vector3 Origin { get; set; }

    /// <summary>
    /// 方向（正規化済み）
    /// </summary>
    public Vector3 Direction { get; set; }

    public Ray(Vector3 origin, Vector3 direction)
    {
        Origin = origin;
        Direction = Vector3.Normalize(direction);
    }

    /// <summary>
    /// レイ上の点を取得
    /// </summary>
    public Vector3 GetPoint(float distance)
    {
        return Origin + Direction * distance;
    }

    public override string ToString()
    {
        return $"Ray(Origin:{Origin}, Direction:{Direction})";
    }
}
