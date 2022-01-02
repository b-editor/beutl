using System.Numerics;

namespace BEditorNext.Graphics.Transformation;

public sealed class SkewTransform : ITransform
{
    public float SkewX { get; set; }

    public float SkewY { get; set; }

    public Matrix3x2 Value => Matrix3x2.CreateSkew(SkewX, SkewY);
}
