namespace BeUtl.Graphics.Transformation;

public sealed class RotationTransform : Transform
{
    public static readonly CoreProperty<float> RotationProperty;
    private float _rotation;

    static RotationTransform()
    {
        RotationProperty = ConfigureProperty<float, RotationTransform>(nameof(Rotation))
            .Accessor(o => o.Rotation, (o, v) => o.Rotation = v)
            .DefaultValue(0)
            .Register();

        AffectRender<RotationTransform>(RotationProperty);
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

    public override Matrix Value => Matrix.CreateRotation(Rotation);

    public static RotationTransform FromDegree(float degree)
    {
        return new RotationTransform(degree * (180.0f / MathF.PI));
    }

    public static RotationTransform FromRadian(float radian)
    {
        return new RotationTransform(radian);
    }
}
