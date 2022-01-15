namespace BEditorNext.Graphics.Transformation;

public sealed class RotationTransform : Transform
{
    private float _rotation;

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
        set => SetProperty(ref _rotation, value);
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
