using Beutl.Graphics;
using Beutl.Graphics.Transformation;

namespace Beutl.Media.Immutable;

public sealed class ImmutableTransform(Matrix matrix, bool isEnabled = true) : ITransform, IEquatable<ITransform?>
{
    public Matrix Value { get; } = matrix;

    public bool IsEnabled { get; } = isEnabled;

    public override bool Equals(object? obj)
    {
        return Equals(obj as ITransform);
    }

    public bool Equals(ITransform? other)
    {
        return other is not null && Value.Equals(other.Value) && IsEnabled == other.IsEnabled;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Value, IsEnabled);
    }

    public static bool operator ==(ImmutableTransform? left, ImmutableTransform? right)
    {
        return EqualityComparer<ImmutableTransform>.Default.Equals(left, right);
    }

    public static bool operator !=(ImmutableTransform? left, ImmutableTransform? right)
    {
        return !(left == right);
    }
}
