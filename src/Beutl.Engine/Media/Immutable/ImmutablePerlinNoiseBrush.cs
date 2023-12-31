using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.Media.Immutable;

public sealed class ImmutablePerlinNoiseBrush(
    float baseFrequencyX,
    float baseFrequencyY,
    int octaves,
    float seed,
    PerlinNoiseType perlinNoiseType,
    float opacity,
    ITransform? transform,
    RelativePoint transformOrigin)
    : IPerlinNoiseBrush, IEquatable<IPerlinNoiseBrush?>
{
    public ImmutablePerlinNoiseBrush(IPerlinNoiseBrush source)
        : this(source.BaseFrequencyX,
              source.BaseFrequencyY,
              source.Octaves,
              source.Seed,
              source.PerlinNoiseType,
              source.Opacity,
              (source.Transform as IMutableTransform)?.ToImmutable() ?? source.Transform,
              source.TransformOrigin)
    {
    }

    public float BaseFrequencyX { get; } = baseFrequencyX;

    public float BaseFrequencyY { get; } = baseFrequencyY;

    public int Octaves { get; } = octaves;

    public float Seed { get; } = seed;

    public PerlinNoiseType PerlinNoiseType { get; } = perlinNoiseType;

    public float Opacity { get; } = opacity;

    public ITransform? Transform { get; } = transform;

    public RelativePoint TransformOrigin { get; } = transformOrigin;

    public override bool Equals(object? obj)
    {
        return Equals(obj as IPerlinNoiseBrush);
    }

    public bool Equals(IPerlinNoiseBrush? other)
    {
        return other is not null
            && BaseFrequencyX == other.BaseFrequencyX
            && BaseFrequencyY == other.BaseFrequencyY
            && Octaves == other.Octaves
            && Seed == other.Seed
            && PerlinNoiseType == other.PerlinNoiseType
            && Opacity == other.Opacity
            && EqualityComparer<ITransform?>.Default.Equals(Transform, other.Transform)
            && TransformOrigin.Equals(other.TransformOrigin);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(BaseFrequencyX, BaseFrequencyY, Octaves, Seed, PerlinNoiseType, Opacity, Transform, TransformOrigin);
    }
}
