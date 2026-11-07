using System.Collections.Immutable;
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

/// <summary>A footprint read at each output point moved by every one of several transforms about an origin, as a zoom or
/// spin toward a center reads.</summary>
/// <remarks>The origin is resolved in the complete input group's bounds, as a pass's bounds origin is. No single affine
/// mapping covers several such reads: one that is far from the origin moves far.</remarks>
internal readonly record struct ShaderOriginInputRegion(RelativePoint Origin, ImmutableArray<Matrix> Transforms)
{
    internal void Validate()
    {
        if (!float.IsFinite(Origin.Point.X) || !float.IsFinite(Origin.Point.Y) || Transforms.IsDefaultOrEmpty)
            throw new ArgumentException("An input region about an origin requires a finite origin and at least one mapping.");
        foreach (Matrix transform in Transforms)
            new ShaderInputRegion(transform).Validate();
    }

    internal Rect Map(Rect output, Rect groupBounds)
    {
        Point origin = groupBounds.Position + Origin.ToPixels(groupBounds.Size);
        Rect centered = output.Translate(-origin);
        Rect result = centered.TransformToAABB(Transforms[0]);
        for (int index = 1; index < Transforms.Length; index++)
            result = result.Union(centered.TransformToAABB(Transforms[index]));
        result = result.Translate(origin);
        RenderRectValidation.ThrowIfInvalidResult(result, "The shader input region exceeds finite coordinates.");
        return result;
    }
}
