using System.ComponentModel.DataAnnotations;

using Beutl.Media.Immutable;

namespace Beutl.Media;

public sealed class PerlinNoiseBrush : Brush, IPerlinNoiseBrush
{
    public static readonly CoreProperty<float> BaseFrequencyXProperty;
    public static readonly CoreProperty<float> BaseFrequencyYProperty;
    public static readonly CoreProperty<int> OctavesProperty;
    public static readonly CoreProperty<float> SeedProperty;
    public static readonly CoreProperty<PerlinNoiseType> PerlinNoiseTypeProperty;
    private float _baseFrequencyX;
    private float _baseFrequencyY;
    private int _octaves;
    private float _seed;
    private PerlinNoiseType _perlinNoiseType;

    static PerlinNoiseBrush()
    {
        BaseFrequencyXProperty = ConfigureProperty<float, PerlinNoiseBrush>(nameof(BaseFrequencyX))
            .Accessor(o => o.BaseFrequencyX, (o, v) => o.BaseFrequencyX = v)
            .Register();

        BaseFrequencyYProperty = ConfigureProperty<float, PerlinNoiseBrush>(nameof(BaseFrequencyY))
            .Accessor(o => o.BaseFrequencyY, (o, v) => o.BaseFrequencyY = v)
            .Register();

        OctavesProperty = ConfigureProperty<int, PerlinNoiseBrush>(nameof(Octaves))
            .Accessor(o => o.Octaves, (o, v) => o.Octaves = v)
            .Register();

        SeedProperty = ConfigureProperty<float, PerlinNoiseBrush>(nameof(Seed))
            .Accessor(o => o.Seed, (o, v) => o.Seed = v)
            .Register();

        PerlinNoiseTypeProperty = ConfigureProperty<PerlinNoiseType, PerlinNoiseBrush>(nameof(PerlinNoiseType))
            .Accessor(o => o.PerlinNoiseType, (o, v) => o.PerlinNoiseType = v)
            .Register();

        AffectsRender<PerlinNoiseBrush>(
            BaseFrequencyXProperty,
            BaseFrequencyYProperty,
            OctavesProperty,
            SeedProperty,
            PerlinNoiseTypeProperty);
    }

    [Range(0, 100)]
    public float BaseFrequencyX
    {
        get => _baseFrequencyX;
        set => SetAndRaise(BaseFrequencyXProperty, ref _baseFrequencyX, value);
    }

    [Range(0, 100)]
    public float BaseFrequencyY
    {
        get => _baseFrequencyY;
        set => SetAndRaise(BaseFrequencyYProperty, ref _baseFrequencyY, value);
    }

    public int Octaves
    {
        get => _octaves;
        set => SetAndRaise(OctavesProperty, ref _octaves, value);
    }

    public float Seed
    {
        get => _seed;
        set => SetAndRaise(SeedProperty, ref _seed, value);
    }

    public PerlinNoiseType PerlinNoiseType
    {
        get => _perlinNoiseType;
        set => SetAndRaise(PerlinNoiseTypeProperty, ref _perlinNoiseType, value);
    }

    public override IBrush ToImmutable()
    {
        return new ImmutablePerlinNoiseBrush(this);
    }
}
