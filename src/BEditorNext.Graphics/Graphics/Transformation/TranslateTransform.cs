namespace BEditorNext.Graphics.Transformation;

public sealed class TranslateTransform : Transform
{
    private float _y;
    private float _x;

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

    public float X
    {
        get => _x;
        set => SetProperty(ref _x, value);
    }

    public float Y
    {
        get => _y;
        set => SetProperty(ref _y, value);
    }

    public override Matrix Value => Matrix.CreateTranslation(X, Y);
}
