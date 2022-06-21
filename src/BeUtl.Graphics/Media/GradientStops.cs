using BeUtl.Media.Immutable;

namespace BeUtl.Media;

/// <summary>
/// A collection of <see cref="GradientStop"/>s.
/// </summary>
public sealed class GradientStops : AffectsRenders<GradientStop>
{
    public IReadOnlyList<ImmutableGradientStop> ToImmutable()
    {
        return this.Select(x => new ImmutableGradientStop(x.Offset, x.Color)).ToArray();
    }
}
