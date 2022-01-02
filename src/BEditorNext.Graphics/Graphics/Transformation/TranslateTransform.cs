using System.Numerics;

namespace BEditorNext.Graphics.Transformation;

public sealed class TranslateTransform : ITransform
{
    public float X { get; set; }

    public float Y { get; set; }

    public Matrix3x2 Value => Matrix3x2.CreateTranslation(X, Y);
}
