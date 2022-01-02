using System.Numerics;

namespace BEditorNext.Graphics.Transformation;

public sealed class RotationTransform : ITransform
{
    public float Rotation { get; set; }

    public Matrix3x2 Value => Matrix3x2.CreateRotation(Rotation);
}
