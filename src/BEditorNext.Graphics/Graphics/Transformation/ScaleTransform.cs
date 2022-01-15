namespace BEditorNext.Graphics.Transformation;

public sealed class ScaleTransform : Transform
{
    private float _scale = 1;
    private float _scaleX = 1;
    private float _scaleY = 1;

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

    public float Scale
    {
        get => _scale;
        set => SetProperty(ref _scale, value);
    }

    public float ScaleX
    {
        get => _scaleX;
        set => SetProperty(ref _scaleX, value);
    }

    public float ScaleY
    {
        get => _scaleY;
        set => SetProperty(ref _scaleY, value);
    }

    public override Matrix Value => Matrix.CreateScale(Scale * ScaleX, Scale * ScaleY);
}
