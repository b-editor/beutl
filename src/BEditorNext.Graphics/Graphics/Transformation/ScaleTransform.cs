using System.Numerics;

namespace BEditorNext.Graphics.Transformation;

public sealed class ScaleTransform : ITransform
{
    public float Scale { get; set; } = 1;

    public float ScaleX { get; set; } = 1;

    public float ScaleY { get; set; } = 1;

    public Matrix3x2 Value => Matrix3x2.CreateScale(Scale * ScaleX, Scale * ScaleY);
}
