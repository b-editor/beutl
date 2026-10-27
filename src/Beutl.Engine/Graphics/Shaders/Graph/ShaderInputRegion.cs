using Beutl.Graphics.Rendering;

namespace Beutl.Graphics.Shaders;

/// <summary>Maps output composition coordinates to a conservative input sampling footprint.</summary>
/// <remarks>The affine mapping is followed by padding in input logical units. Declare the complete
/// shader sampling footprint, including derivatives and neighborhood reads. Sampler filtering and raster
/// rounding are added by the renderer. This is an author contract, not shader-source inference.</remarks>
public readonly record struct ShaderInputRegion(Matrix Transform, float Padding = 0)
{
    public static ShaderInputRegion Identity => new(Matrix.Identity);

    internal void Validate()
    {
        if (!float.IsFinite(Padding) || Padding < 0
            || !float.IsFinite(Transform.M11) || !float.IsFinite(Transform.M12)
            || !float.IsFinite(Transform.M21) || !float.IsFinite(Transform.M22)
            || !float.IsFinite(Transform.M31) || !float.IsFinite(Transform.M32)
            || Transform.M13 != 0 || Transform.M23 != 0 || Transform.M33 != 1)
            throw new ArgumentException("An input region requires a finite affine mapping and non-negative padding.");
    }

    internal Rect Map(Rect output)
    {
        Rect result = output.TransformToAABB(Transform).Inflate(Padding);
        RenderRectValidation.ThrowIfInvalidResult(result, "The shader input region exceeds finite coordinates.");
        return result;
    }
}
