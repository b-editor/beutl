namespace BEditorNext.Graphics.Transformation;

public sealed class TranslateTransform : ITransform
{
    public float X { get; set; }

    public float Y { get; set; }

    public Matrix Value => Matrix.CreateTranslation(X, Y);
}
