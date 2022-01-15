namespace BeUtl.Graphics.Transformation;

public sealed class ScaleTransform : ITransform
{
    public ScaleTransform()
    {
    }

    public ScaleTransform(Vector vector, float scale = 1)
    {
        Scale = scale;
        ScaleX = vector.X;
        ScaleY = vector.Y;
    }

    public ScaleTransform(float x, float y, float scale = 1)
    {
        Scale = scale;
        ScaleX = x;
        ScaleY = y;
    }

    public float Scale { get; set; } = 1;

    public float ScaleX { get; set; } = 1;

    public float ScaleY { get; set; } = 1;

    public Matrix Value => Matrix.CreateScale(Scale * ScaleX, Scale * ScaleY);
}
