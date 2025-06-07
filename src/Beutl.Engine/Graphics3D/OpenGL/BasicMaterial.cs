using System.Numerics;

namespace Beutl.Graphics.Rendering.OpenGL;

/// <summary>
/// 基本的な3Dマテリアルの実装
/// </summary>
public class BasicMaterial : I3DMaterial
{
    public Vector3 Albedo { get; set; } = Vector3.One;
    public float Metallic { get; set; } = 0.0f;
    public float Roughness { get; set; } = 0.5f;
    public Vector3 Emission { get; set; } = Vector3.Zero;
    public ITexture? AlbedoTexture { get; set; }
    public ITexture? NormalTexture { get; set; }
    public ITexture? MetallicRoughnessTexture { get; set; }

    public BasicMaterial()
    {
    }

    public BasicMaterial(Vector3 albedo, float metallic = 0.0f, float roughness = 0.5f)
    {
        Albedo = albedo;
        Metallic = metallic;
        Roughness = roughness;
    }

    /// <summary>
    /// 金属マテリアルを作成
    /// </summary>
    public static BasicMaterial CreateMetal(Vector3 color, float roughness = 0.1f)
    {
        return new BasicMaterial(color, 1.0f, roughness);
    }

    /// <summary>
    /// 非金属マテリアルを作成
    /// </summary>
    public static BasicMaterial CreateDielectric(Vector3 color, float roughness = 0.5f)
    {
        return new BasicMaterial(color, 0.0f, roughness);
    }

    /// <summary>
    /// 発光マテリアルを作成
    /// </summary>
    public static BasicMaterial CreateEmissive(Vector3 color, Vector3 emission)
    {
        return new BasicMaterial(color) { Emission = emission };
    }

    /// <summary>
    /// 事前定義されたマテリアル：金
    /// </summary>
    public static BasicMaterial Gold => CreateMetal(new Vector3(1.0f, 0.766f, 0.336f), 0.1f);

    /// <summary>
    /// 事前定義されたマテリアル：銀
    /// </summary>
    public static BasicMaterial Silver => CreateMetal(new Vector3(0.972f, 0.960f, 0.915f), 0.05f);

    /// <summary>
    /// 事前定義されたマテリアル：銅
    /// </summary>
    public static BasicMaterial Copper => CreateMetal(new Vector3(0.955f, 0.637f, 0.538f), 0.15f);

    /// <summary>
    /// 事前定義されたマテリアル：鉄
    /// </summary>
    public static BasicMaterial Iron => CreateMetal(new Vector3(0.560f, 0.570f, 0.580f), 0.3f);

    /// <summary>
    /// 事前定義されたマテリアル：白いプラスチック
    /// </summary>
    public static BasicMaterial WhitePlastic => CreateDielectric(new Vector3(0.9f, 0.9f, 0.9f), 0.8f);

    /// <summary>
    /// 事前定義されたマテリアル：黒いプラスチック
    /// </summary>
    public static BasicMaterial BlackPlastic => CreateDielectric(new Vector3(0.1f, 0.1f, 0.1f), 0.9f);

    /// <summary>
    /// 事前定義されたマテリアル：赤いプラスチック
    /// </summary>
    public static BasicMaterial RedPlastic => CreateDielectric(new Vector3(0.8f, 0.1f, 0.1f), 0.7f);

    /// <summary>
    /// 事前定義されたマテリアル：青いプラスチック
    /// </summary>
    public static BasicMaterial BluePlastic => CreateDielectric(new Vector3(0.1f, 0.1f, 0.8f), 0.7f);

    /// <summary>
    /// 事前定義されたマテリアル：緑のプラスチック
    /// </summary>
    public static BasicMaterial GreenPlastic => CreateDielectric(new Vector3(0.1f, 0.8f, 0.1f), 0.7f);

    /// <summary>
    /// 事前定義されたマテリアル：ガラス
    /// </summary>
    public static BasicMaterial Glass => CreateDielectric(new Vector3(0.95f, 0.95f, 0.95f), 0.0f);

    /// <summary>
    /// 事前定義されたマテリアル：ゴム
    /// </summary>
    public static BasicMaterial Rubber => CreateDielectric(new Vector3(0.2f, 0.2f, 0.2f), 1.0f);
}
