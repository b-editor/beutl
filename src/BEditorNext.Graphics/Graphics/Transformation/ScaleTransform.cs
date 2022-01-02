namespace BEditorNext.Graphics.Transformation;

public sealed class ScaleTransform : ITransform
{
    public float Scale { get; set; } = 1;

    public float ScaleX { get; set; } = 1;

    public float ScaleY { get; set; } = 1;

    public Matrix Value => Matrix.CreateScale(Scale * ScaleX, Scale * ScaleY);
}
