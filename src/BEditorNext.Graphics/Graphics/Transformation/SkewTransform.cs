namespace BEditorNext.Graphics.Transformation;

public sealed class SkewTransform : ITransform
{
    public float SkewX { get; set; }

    public float SkewY { get; set; }

    public Matrix Value => Matrix.CreateSkew(SkewX, SkewY);
}
