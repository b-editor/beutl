using System.Numerics;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 環境光設定
/// </summary>
public class AmbientLight
{
    /// <summary>
    /// 環境光の色
    /// </summary>
    public Vector3 Color { get; set; } = new(0.2f, 0.2f, 0.2f);

    /// <summary>
    /// 環境光の強度
    /// </summary>
    public float Intensity { get; set; } = 0.1f;

    /// <summary>
    /// 環境光が有効かどうか
    /// </summary>
    public bool Enabled { get; set; } = true;
}
