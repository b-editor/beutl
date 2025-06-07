using System.Numerics;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 点光源
/// </summary>
public class PointLight : ILight
{
    public LightType Type => LightType.Point;
    public Vector3 Color { get; set; } = Vector3.One;
    public float Intensity { get; set; } = 1.0f;
    public bool Enabled { get; set; } = true;
    public bool CastShadows { get; set; } = true;

    /// <summary>
    /// ライトの位置
    /// </summary>
    public Vector3 Position { get; set; } = Vector3.Zero;

    /// <summary>
    /// ライトの影響範囲
    /// </summary>
    public float Range { get; set; } = 10.0f;

    /// <summary>
    /// 減衰パラメータ（定数項）
    /// </summary>
    public float AttenuationConstant { get; set; } = 1.0f;

    /// <summary>
    /// 減衰パラメータ（一次項）
    /// </summary>
    public float AttenuationLinear { get; set; } = 0.09f;

    /// <summary>
    /// 減衰パラメータ（二次項）
    /// </summary>
    public float AttenuationQuadratic { get; set; } = 0.032f;

    /// <summary>
    /// シャドウマップのサイズ
    /// </summary>
    public int ShadowMapSize { get; set; } = 1024;

    /// <summary>
    /// シャドウバイアス
    /// </summary>
    public float ShadowBias { get; set; } = 0.001f;
}
