namespace BEditorNext.Graphics.Transformation;

public sealed class RotationTransform : ITransform
{
    public float Rotation { get; set; }

    public Matrix Value => Matrix.CreateRotation(Rotation);
}
