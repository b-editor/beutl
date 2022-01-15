namespace BeUtl.Graphics.Transformation;

public sealed class TranslateTransform : ITransform
{
    public TranslateTransform()
    {
    }

    public TranslateTransform(float x, float y)
    {
        X = x;
        Y = y;
    }

    public TranslateTransform(Vector vector)
    {
        X = vector.X;
        Y = vector.Y;
    }

    public TranslateTransform(Point point)
    {
        X = point.X;
        Y = point.Y;
    }

    public float X { get; set; }

    public float Y { get; set; }

    public Matrix Value => Matrix.CreateTranslation(X, Y);
}
