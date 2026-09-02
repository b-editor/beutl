namespace Beutl.Graphics.Transformation;

internal sealed record CanonicalTransformLayoutResult(
    TranslateTransform Translate,
    ScaleTransform Scale,
    RotationTransform Rotation,
    TransformGroup Group,
    bool StructureChanged)
{
    /// <summary>Matrix applied after the operative T in application order (Identity under the new [T, R, S]).</summary>
    internal Matrix PostMatrixOfT { get; init; } = Matrix.Identity;
}
