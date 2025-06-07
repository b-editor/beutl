using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// PBRマテリアル（物理ベースレンダリング）
/// </summary>
public class PbrMaterial3D : Material3D
{
    public static readonly CoreProperty<Vector3> AlbedoProperty;
    public static readonly CoreProperty<float> MetallicProperty;
    public static readonly CoreProperty<float> RoughnessProperty;
    public static readonly CoreProperty<Vector3> EmissionProperty;
    public static readonly CoreProperty<float> OpacityProperty;
    public static readonly CoreProperty<ITextureResource?> AlbedoTextureProperty;
    public static readonly CoreProperty<ITextureResource?> NormalTextureProperty;
    public static readonly CoreProperty<ITextureResource?> MetallicRoughnessTextureProperty;
    public static readonly CoreProperty<ITextureResource?> EmissionTextureProperty;

    private Vector3 _albedo = Vector3.One;
    private float _metallic = 0.0f;
    private float _roughness = 0.5f;
    private Vector3 _emission = Vector3.Zero;
    private float _opacity = 1.0f;
    private ITextureResource? _albedoTexture;
    private ITextureResource? _normalTexture;
    private ITextureResource? _metallicRoughnessTexture;
    private ITextureResource? _emissionTexture;

    static PbrMaterial3D()
    {
        AlbedoProperty = ConfigureProperty<Vector3, PbrMaterial3D>(nameof(Albedo))
            .Accessor(o => o.Albedo, (o, v) => o.Albedo = v)
            .DefaultValue(Vector3.One)
            .Register();

        MetallicProperty = ConfigureProperty<float, PbrMaterial3D>(nameof(Metallic))
            .Accessor(o => o.Metallic, (o, v) => o.Metallic = v)
            .DefaultValue(0.0f)
            .Register();

        RoughnessProperty = ConfigureProperty<float, PbrMaterial3D>(nameof(Roughness))
            .Accessor(o => o.Roughness, (o, v) => o.Roughness = v)
            .DefaultValue(0.5f)
            .Register();

        EmissionProperty = ConfigureProperty<Vector3, PbrMaterial3D>(nameof(Emission))
            .Accessor(o => o.Emission, (o, v) => o.Emission = v)
            .DefaultValue(Vector3.Zero)
            .Register();

        OpacityProperty = ConfigureProperty<float, PbrMaterial3D>(nameof(Opacity))
            .Accessor(o => o.Opacity, (o, v) => o.Opacity = v)
            .DefaultValue(1.0f)
            .Register();

        AlbedoTextureProperty = ConfigureProperty<ITextureResource?, PbrMaterial3D>(nameof(AlbedoTexture))
            .Accessor(o => o.AlbedoTexture, (o, v) => o.AlbedoTexture = v)
            .DefaultValue(null)
            .Register();

        NormalTextureProperty = ConfigureProperty<ITextureResource?, PbrMaterial3D>(nameof(NormalTexture))
            .Accessor(o => o.NormalTexture, (o, v) => o.NormalTexture = v)
            .DefaultValue(null)
            .Register();

        MetallicRoughnessTextureProperty = ConfigureProperty<ITextureResource?, PbrMaterial3D>(nameof(MetallicRoughnessTexture))
            .Accessor(o => o.MetallicRoughnessTexture, (o, v) => o.MetallicRoughnessTexture = v)
            .DefaultValue(null)
            .Register();

        EmissionTextureProperty = ConfigureProperty<ITextureResource?, PbrMaterial3D>(nameof(EmissionTexture))
            .Accessor(o => o.EmissionTexture, (o, v) => o.EmissionTexture = v)
            .DefaultValue(null)
            .Register();

        AffectsRender(AlbedoProperty, MetallicProperty, RoughnessProperty, EmissionProperty, OpacityProperty,
            AlbedoTextureProperty, NormalTextureProperty, MetallicRoughnessTextureProperty, EmissionTextureProperty);
    }

    /// <summary>
    /// ベースカラー（拡散反射色）
    /// </summary>
    [Display(Name = nameof(Strings.Albedo), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Material))]
    public Vector3 Albedo
    {
        get => _albedo;
        set => SetAndRaise(AlbedoProperty, ref _albedo, Vector3.Clamp(value, Vector3.Zero, Vector3.One));
    }

    /// <summary>
    /// 金属性（0.0=非金属, 1.0=金属）
    /// </summary>
    [Display(Name = nameof(Strings.Metallic), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Material))]
    [Range(0.0f, 1.0f)]
    public float Metallic
    {
        get => _metallic;
        set => SetAndRaise(MetallicProperty, ref _metallic, Math.Clamp(value, 0.0f, 1.0f));
    }

    /// <summary>
    /// 粗さ（0.0=鏡面, 1.0=完全拡散）
    /// </summary>
    [Display(Name = nameof(Strings.Roughness), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Material))]
    [Range(0.0f, 1.0f)]
    public float Roughness
    {
        get => _roughness;
        set => SetAndRaise(RoughnessProperty, ref _roughness, Math.Clamp(value, 0.0f, 1.0f));
    }

    /// <summary>
    /// 発光色
    /// </summary>
    [Display(Name = nameof(Strings.Emission), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Material))]
    public Vector3 Emission
    {
        get => _emission;
        set => SetAndRaise(EmissionProperty, ref _emission, Vector3.Max(value, Vector3.Zero));
    }

    /// <summary>
    /// 不透明度
    /// </summary>
    [Display(Name = nameof(Strings.Opacity), ResourceType = typeof(Strings))]
    [Range(0.0f, 1.0f)]
    public float Opacity
    {
        get => _opacity;
        set => SetAndRaise(OpacityProperty, ref _opacity, Math.Clamp(value, 0.0f, 1.0f));
    }

    /// <summary>
    /// アルベドテクスチャ
    /// </summary>
    [Display(Name = nameof(Strings.AlbedoTexture), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Textures))]
    public ITextureResource? AlbedoTexture
    {
        get => _albedoTexture;
        set => SetAndRaise(AlbedoTextureProperty, ref _albedoTexture, value);
    }

    /// <summary>
    /// 法線テクスチャ
    /// </summary>
    [Display(Name = nameof(Strings.NormalTexture), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Textures))]
    public ITextureResource? NormalTexture
    {
        get => _normalTexture;
        set => SetAndRaise(NormalTextureProperty, ref _normalTexture, value);
    }

    /// <summary>
    /// メタリック・ラフネステクスチャ
    /// </summary>
    [Display(Name = nameof(Strings.MetallicRoughnessTexture), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Textures))]
    public ITextureResource? MetallicRoughnessTexture
    {
        get => _metallicRoughnessTexture;
        set => SetAndRaise(MetallicRoughnessTextureProperty, ref _metallicRoughnessTexture, value);
    }

    /// <summary>
    /// 発光テクスチャ
    /// </summary>
    [Display(Name = nameof(Strings.EmissionTexture), ResourceType = typeof(Strings),
        GroupName = nameof(Strings.Textures))]
    public ITextureResource? EmissionTexture
    {
        get => _emissionTexture;
        set => SetAndRaise(EmissionTextureProperty, ref _emissionTexture, value);
    }

    public override BasicMaterial ToBasicMaterial()
    {
        return new BasicMaterial
        {
            Albedo = _albedo,
            Metallic = _metallic,
            Roughness = _roughness,
            Emission = _emission,
            AlbedoTexture = _albedoTexture,
            NormalTexture = _normalTexture,
            MetallicRoughnessTexture = _metallicRoughnessTexture
        };
    }

    /// <summary>
    /// マテリアルプリセットを作成
    /// </summary>
    public static PbrMaterial3D CreateMetal(Vector3 albedo, float roughness = 0.1f)
    {
        return new PbrMaterial3D
        {
            Name = "Metal",
            Albedo = albedo,
            Metallic = 1.0f,
            Roughness = roughness
        };
    }

    public static PbrMaterial3D CreateDielectric(Vector3 albedo, float roughness = 0.5f)
    {
        return new PbrMaterial3D
        {
            Name = "Dielectric",
            Albedo = albedo,
            Metallic = 0.0f,
            Roughness = roughness
        };
    }

    public static PbrMaterial3D CreateGlass(Vector3 albedo, float roughness = 0.0f)
    {
        return new PbrMaterial3D
        {
            Name = "Glass",
            Albedo = albedo,
            Metallic = 0.0f,
            Roughness = roughness,
            Opacity = 0.9f
        };
    }

    public static PbrMaterial3D CreateEmissive(Vector3 emission, float intensity = 1.0f)
    {
        return new PbrMaterial3D
        {
            Name = "Emissive",
            Albedo = Vector3.Zero,
            Metallic = 0.0f,
            Roughness = 1.0f,
            Emission = emission * intensity
        };
    }

    /// <summary>
    /// レンダリング無効化処理
    /// </summary>
    private static void AffectsRender(params CoreProperty[] properties)
    {
        foreach (var property in properties)
        {
            property.Changed.Subscribe(e =>
            {
                if (e.Sender is PbrMaterial3D material)
                {
                    material.RaiseInvalidated();
                }
            });
        }
    }

    private void RaiseInvalidated()
    {
        Invalidated?.Invoke(this, new RenderInvalidatedEventArgs(this));
    }
}
