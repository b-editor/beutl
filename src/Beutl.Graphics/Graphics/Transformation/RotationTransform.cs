using Beutl.Utilities;

namespace Beutl.Graphics.Transformation;

public sealed class RotationTransform : Transform
{
    public static readonly CoreProperty<float> RotationProperty;
    private float _rotation;

    static RotationTransform()
    {
        RotationProperty = ConfigureProperty<float, RotationTransform>(nameof(Rotation))
            .Accessor(o => o.Rotation, (o, v) => o.Rotation = v)
            .DefaultValue(0)
            .PropertyFlags(PropertyFlags.All)
            .SerializeName("rotation")
            .Register();

        AffectsRender<RotationTransform>(RotationProperty);
    }

    public RotationTransform()
    {
    }

    public RotationTransform(float rotation)
    {
        Rotation = rotation;
    }

    public float Rotation
    {
        get => _rotation;
        set => SetAndRaise(RotationProperty, ref _rotation, value);
    }

    public override Matrix Value => Matrix.CreateRotation(MathUtilities.ToRadians(_rotation));

    public static RotationTransform FromRadians(float radians)
    {
        return new RotationTransform(MathUtilities.ToDegrees(radians));
    }
}
