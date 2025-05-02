using Beutl.Media;

namespace Beutl.Graphics3D;

public class StandardMaterial : Material
{
    public static readonly CoreProperty<Color> AlbedoProperty;
    public static readonly CoreProperty<Color> SpecularProperty;
    public static readonly CoreProperty<float> MetallicProperty;
    public static readonly CoreProperty<float> SmoothnessProperty;
    public static readonly CoreProperty<ColorF> EmissionProperty;
    public static readonly CoreProperty<TextureSource?> HeightMapProperty;
    public static readonly CoreProperty<TextureSource?> NormalMapProperty;
    private Color _albedo;
    private Color _specular;
    private float _metallic;
    private float _smoothness;
    private ColorF _emission;
    private TextureSource _heightMap;

    static StandardMaterial()
    {
        AlbedoProperty = ConfigureProperty<Color, StandardMaterial>(nameof(Albedo))
            .Accessor(o => o.Albedo, (o, v) => o.Albedo = v)
            .DefaultValue(Colors.White)
            .Register();

        SpecularProperty = ConfigureProperty<Color, StandardMaterial>(nameof(Specular))
            .Accessor(o => o.Specular, (o, v) => o.Specular = v)
            .DefaultValue(Colors.White)
            .Register();

        MetallicProperty = ConfigureProperty<float, StandardMaterial>(nameof(Metallic))
            .Accessor(o => o.Metallic, (o, v) => o.Metallic = v)
            .DefaultValue(0.0F)
            .Register();

        SmoothnessProperty = ConfigureProperty<float, StandardMaterial>(nameof(Smoothness))
            .Accessor(o => o.Smoothness, (o, v) => o.Smoothness = v)
            .DefaultValue(0.0F)
            .Register();

        EmissionProperty = ConfigureProperty<ColorF, StandardMaterial>(nameof(Emission))
            .Accessor(o => o.Emission, (o, v) => o.Emission = v)
            .DefaultValue(new ColorF
            {
                A = 1,
                R = 1,
                G = 1,
                B = 1
            })
            .Register();

        HeightMapProperty = ConfigureProperty<TextureSource?, StandardMaterial>(nameof(HeightMap))
            .Accessor(o => o.HeightMap, (o, v) => o.HeightMap = v)
            .DefaultValue(null)
            .Register();

        // AffectsRender<SpecularMaterial>(ColorProperty);
    }

    public Color Albedo
    {
        get => _albedo;
        set => SetAndRaise(AlbedoProperty, ref _albedo, value);
    }

    public Color Specular
    {
        get => _specular;
        set => SetAndRaise(SpecularProperty, ref _specular, value);
    }

    public float Metallic
    {
        get => _metallic;
        set => SetAndRaise(MetallicProperty, ref _metallic, value);
    }

    public float Smoothness
    {
        get => _smoothness;
        set => SetAndRaise(SmoothnessProperty, ref _smoothness, value);
    }

    public ColorF Emission
    {
        get => _emission;
        set => SetAndRaise(EmissionProperty, ref _emission, value);
    }

    public TextureSource HeightMap
    {
        get => _heightMap;
        set => SetAndRaise(HeightMapProperty, ref _heightMap, value);
    }
}
