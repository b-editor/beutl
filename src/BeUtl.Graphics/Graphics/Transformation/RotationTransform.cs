namespace BeUtl.Graphics.Transformation;

public sealed class RotationTransform : ITransform
{
    public RotationTransform()
    {
    }

    public RotationTransform(float rotation)
    {
        Rotation = rotation;
    }

    public float Rotation { get; set; }

    public Matrix Value => Matrix.CreateRotation(Rotation);

    public static RotationTransform FromDegree(float degree)
    {
        return new RotationTransform(degree * (180.0f / MathF.PI));
    }

    public static RotationTransform FromRadian(float radian)
    {
        return new RotationTransform(radian);
    }
}
