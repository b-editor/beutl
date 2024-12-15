using System.Numerics;

namespace Beutl.Graphics3D.Transformation;

public class ImmutableTransform3D(Matrix4x4 value) : ITransform3D, IEquatable<ITransform3D?>
{
    public Matrix4x4 Value { get; } = value;
    
    public bool Equals(ITransform3D? other)
    {
        return other is not null && (ReferenceEquals(this, other) || Value.Equals(other.Value));
    }
    
    public override bool Equals(object? obj) => Equals(obj as ITransform3D);

    public override int GetHashCode() => Value.GetHashCode();
}
