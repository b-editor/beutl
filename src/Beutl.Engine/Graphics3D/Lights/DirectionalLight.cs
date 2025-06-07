using System.Numerics;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 方向光源（太陽光など）
/// </summary>
public class DirectionalLight : ILight
{
    public LightType Type => LightType.Directional;
    public Vector3 Color { get; set; } = Vector3.One;
    public float Intensity { get; set; } = 1.0f;
    public bool Enabled { get; set; } = true;
    public bool CastShadows { get; set; } = true;

    /// <summary>
    /// ライトの方向（正規化されたベクトル）
    /// </summary>
    public Vector3 Direction { get; set; } = new(0, -1, 0);

    /// <summary>
    /// カスケードシャドウマップの分割距離
    /// </summary>
    public float[] CascadeSplits { get; set; } = [0.1f, 10f, 50f, 200f];

    /// <summary>
    /// シャドウマップのサイズ
    /// </summary>
    public int ShadowMapSize { get; set; } = 2048;

    /// <summary>
    /// シャドウバイアス
    /// </summary>
    public float ShadowBias { get; set; } = 0.001f;
}
