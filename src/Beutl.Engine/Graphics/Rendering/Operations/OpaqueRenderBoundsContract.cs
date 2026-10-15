namespace Beutl.Graphics.Rendering;

public sealed class OpaqueRenderBoundsContract
{
    private readonly Rect _sourceBounds;

    private OpaqueRenderBoundsContract(Rect sourceBounds, Thickness rasterOutset)
    {
        Kind = OpaqueRenderBoundsKind.Source;
        _sourceBounds = sourceBounds;
        RasterOutset = rasterOutset;
        StructuralIdentity = new OpaqueRenderBoundsStructuralIdentity(Kind, null, null);
    }

    /// <summary>
    /// The logical room this source's rasterization needs beyond the bounds it publishes, on each side.
    /// </summary>
    /// <remarks>
    /// Publishing the wider rectangle instead would move it: the bounds a fragment publishes are what places
    /// it, so anything scale-dependent in them changes a project's composition between preview and export.
    /// The outset therefore only widens the buffer the source draws into; nothing downstream sees it.
    /// </remarks>
    public Thickness RasterOutset { get; }

    /// <param name="outputBounds">The bounds this source publishes, which place it.</param>
    /// <param name="rasterOutset">
    /// Extra logical room per side for the buffer only, for a source whose rasterization reaches outside the
    /// bounds it publishes. Must be non-negative and finite.
    /// </param>
    /// <remarks>
    /// Both values are fixed when this contract is built. A source whose place, size, or outset moves per frame
    /// builds its <see cref="OpaqueRenderDescription"/> inside <see cref="RenderNode.Process"/>, which still
    /// compiles one plan, because this contract contributes only its kind to the structural identity.
    /// </remarks>
    public static OpaqueRenderBoundsContract Source(Rect outputBounds, Thickness rasterOutset = default)
    {
        RenderRectValidation.ThrowIfInvalidInput(outputBounds, nameof(outputBounds));
        if (!IsUsableOutset(rasterOutset))
        {
            throw new ArgumentOutOfRangeException(
                nameof(rasterOutset),
                rasterOutset,
                "A raster outset must be finite and non-negative on every side.");
        }

        return new OpaqueRenderBoundsContract(outputBounds, rasterOutset);
    }

    private static bool IsUsableOutset(Thickness outset)
        => float.IsFinite(outset.Left)
           && float.IsFinite(outset.Top)
           && float.IsFinite(outset.Right)
           && float.IsFinite(outset.Bottom)
           && outset.Left >= 0
           && outset.Top >= 0
           && outset.Right >= 0
           && outset.Bottom >= 0;

    internal OpaqueRenderBoundsKind Kind { get; }

    internal object StructuralIdentity { get; }

    internal Rect TransformBounds(IReadOnlyList<Rect> inputBounds)
    {
        ArgumentNullException.ThrowIfNull(inputBounds);
        ValidateRectangles(inputBounds, nameof(inputBounds));

        if (inputBounds.Count != 0)
            throw new InvalidOperationException("A source bounds contract cannot receive input bounds.");

        RenderRectValidation.ThrowIfInvalidResult(
            _sourceBounds,
            "The opaque render bounds forward mapping returned an invalid rectangle.");
        return _sourceBounds;
    }

    internal IReadOnlyList<Rect> GetRequiredInputBounds(
        Rect requestedOutputBounds,
        IReadOnlyList<Rect> inputBounds)
    {
        RenderRectValidation.ThrowIfInvalidInput(requestedOutputBounds, nameof(requestedOutputBounds));
        ArgumentNullException.ThrowIfNull(inputBounds);
        ValidateRectangles(inputBounds, nameof(inputBounds));

        if (inputBounds.Count != 0)
            throw new InvalidOperationException("A source bounds contract cannot receive input bounds.");

        return Array.Empty<Rect>();
    }

    internal void ThrowIfIncompatible(OpaqueRenderTopology topology, string parameterName)
    {
        bool compatible = topology switch
        {
            OpaqueRenderTopology.Source => Kind == OpaqueRenderBoundsKind.Source,
            OpaqueRenderTopology.Map => Kind == OpaqueRenderBoundsKind.Map,
            OpaqueRenderTopology.Combine or OpaqueRenderTopology.Expand =>
                Kind is OpaqueRenderBoundsKind.Combine or OpaqueRenderBoundsKind.FullInputs,
            _ => false,
        };

        if (!compatible)
        {
            throw new ArgumentException(
                $"The {Kind} bounds contract is incompatible with {topology} topology.",
                parameterName);
        }
    }

    private static void ValidateRectangles(IReadOnlyList<Rect> values, string parameterName)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (!RenderRectValidation.IsFiniteNonNegative(values[index]))
            {
                throw new ArgumentException(
                    $"Input bound {index} must be finite and have non-negative dimensions.",
                    parameterName);
            }
        }
    }
}

internal readonly record struct OpaqueRenderBoundsStructuralIdentity(
    OpaqueRenderBoundsKind Kind,
    object? ForwardIdentity,
    object? BackwardIdentity);
