using System.Numerics;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// ライトの基底インターフェース
/// </summary>
public interface ILight
{
    /// <summary>
    /// ライトの種類
    /// </summary>
    LightType Type { get; }

    /// <summary>
    /// ライトの色
    /// </summary>
    Vector3 Color { get; }

    /// <summary>
    /// ライトの強度
    /// </summary>
    float Intensity { get; }

    /// <summary>
    /// ライトが有効かどうか
    /// </summary>
    bool Enabled { get; }

    /// <summary>
    /// シャドウを投影するかどうか
    /// </summary>
    bool CastShadows { get; }
}
