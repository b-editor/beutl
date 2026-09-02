using System.Collections.ObjectModel;

namespace Beutl.Graphics.Rendering;

public sealed class OpaqueRenderBoundsContract
{
    private readonly Rect _sourceBounds;
    private readonly RenderBoundsContract _mapBounds;
    private readonly Func<IReadOnlyList<Rect>, Rect>? _transformBounds;
    private readonly Func<Rect, IReadOnlyList<Rect>, IReadOnlyList<Rect>>? _getRequiredInputBounds;

    private OpaqueRenderBoundsContract(Rect sourceBounds, Thickness rasterOutset)
    {
        Kind = OpaqueRenderBoundsKind.Source;
        _sourceBounds = sourceBounds;
        RasterOutset = rasterOutset;
        StructuralIdentity = new OpaqueRenderBoundsStructuralIdentity(Kind, null, null);
    }

    private OpaqueRenderBoundsContract(RenderBoundsContract mapBounds)
    {
        Kind = OpaqueRenderBoundsKind.Map;
        _mapBounds = mapBounds;
        StructuralIdentity = new OpaqueRenderBoundsStructuralIdentity(
            Kind,
            mapBounds.StructuralIdentity,
            null);
    }

    private OpaqueRenderBoundsContract(
        OpaqueRenderBoundsKind kind,
        Func<IReadOnlyList<Rect>, Rect> transformBounds,
        Func<Rect, IReadOnlyList<Rect>, IReadOnlyList<Rect>>? getRequiredInputBounds,
        Delegate? forwardIdentity = null,
        Delegate? backwardIdentity = null)
    {
        Kind = kind;
        _transformBounds = transformBounds;
        _getRequiredInputBounds = getRequiredInputBounds;
        // A state-passing factory names the author's own callback here; a capturing one has none to name and
        // the invoked delegate is the author's. Declaring these Delegate rather than object is what keeps a
        // future factory from putting a per-recording binding into the identity by omitting the argument.
        Delegate? backward = backwardIdentity ?? getRequiredInputBounds;
        StructuralIdentity = new OpaqueRenderBoundsStructuralIdentity(
            kind,
            RenderDescriptionValidation.StructuralIdentityOf(forwardIdentity ?? transformBounds),
            backward is null ? null : RenderDescriptionValidation.StructuralIdentityOf(backward));
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
    /// <para>
    /// Both values answer from the moment this contract is built, and the state a call supplies never reaches
    /// them. The state-passing overloads of <see cref="Combine{TState}"/> and <see cref="FullInputs{TState}"/>
    /// are no exception: they bind their state here too, and exist only so a mapping the engine invokes later
    /// can stay <see langword="static"/>-declared. A bounds contract is operation shape either way.
    /// </para>
    /// <para>
    /// A source whose place, size, or outset is a per-recording value therefore builds its
    /// <see cref="OpaqueRenderDescription"/> inside <see cref="RenderNode.Process"/>, over the values it is
    /// moving by. That costs no plan: this contract contributes only its kind to the structural identity,
    /// and an execution callback bound to the node that declares it contributes its method, so two nodes of
    /// one type standing at different places compile one plan and re-run it over their own rectangles. A
    /// source that draws outside the rectangle its description declared fails at
    /// <see cref="OpaqueRenderSession.CreateOutput"/> rather than escaping the bounds it published.
    /// </para>
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

    public static OpaqueRenderBoundsContract Map(RenderBoundsContract bounds)
    {
        bounds.ThrowIfUninitialized(nameof(bounds));
        return new OpaqueRenderBoundsContract(bounds);
    }

    public static OpaqueRenderBoundsContract Combine(
        Func<IReadOnlyList<Rect>, Rect> transformBounds,
        Func<Rect, IReadOnlyList<Rect>, IReadOnlyList<Rect>> getRequiredInputBounds)
    {
        ArgumentNullException.ThrowIfNull(transformBounds);
        ArgumentNullException.ThrowIfNull(getRequiredInputBounds);
        RenderDescriptionValidation.ValidatePureMetadataCallback(transformBounds, nameof(transformBounds));
        RenderDescriptionValidation.ValidatePureMetadataCallback(
            getRequiredInputBounds,
            nameof(getRequiredInputBounds));
        return new OpaqueRenderBoundsContract(
            OpaqueRenderBoundsKind.Combine,
            transformBounds,
            getRequiredInputBounds);
    }

    public static OpaqueRenderBoundsContract FullInputs(
        Func<IReadOnlyList<Rect>, Rect> transformBounds)
    {
        ArgumentNullException.ThrowIfNull(transformBounds);
        RenderDescriptionValidation.ValidatePureMetadataCallback(transformBounds, nameof(transformBounds));
        return new OpaqueRenderBoundsContract(
            OpaqueRenderBoundsKind.FullInputs,
            transformBounds,
            null);
    }

    /// <summary>
    /// Creates a combining bounds contract whose mappings read call-owned state instead of closing over it.
    /// </summary>
    /// <typeparam name="TState">The immutable state the mappings read.</typeparam>
    /// <param name="state">The per-recording values the mappings need, which are request data.</param>
    /// <param name="transformBounds">A pure forward mapping, declared <see langword="static"/>.</param>
    /// <param name="getRequiredInputBounds">A pure backward mapping, declared the same way.</param>
    public static OpaqueRenderBoundsContract Combine<TState>(
        TState state,
        Func<TState, IReadOnlyList<Rect>, Rect> transformBounds,
        Func<TState, Rect, IReadOnlyList<Rect>, IReadOnlyList<Rect>> getRequiredInputBounds)
    {
        ArgumentNullException.ThrowIfNull(transformBounds);
        ArgumentNullException.ThrowIfNull(getRequiredInputBounds);
        RenderDescriptionValidation.ValidatePureMetadataCallback(transformBounds, nameof(transformBounds));
        RenderDescriptionValidation.ValidatePureMetadataCallback(
            getRequiredInputBounds,
            nameof(getRequiredInputBounds));
        var binding = new CombineBoundsMapping<TState>(state, transformBounds, getRequiredInputBounds);
        return new OpaqueRenderBoundsContract(
            OpaqueRenderBoundsKind.Combine,
            binding.TransformBounds,
            binding.GetRequiredInputBounds,
            transformBounds,
            getRequiredInputBounds);
    }

    /// <summary>
    /// Creates a full-inputs bounds contract whose forward mapping reads call-owned state.
    /// </summary>
    /// <typeparam name="TState">The immutable state the mapping reads.</typeparam>
    /// <param name="state">The per-recording values the mapping needs, which are request data.</param>
    /// <param name="transformBounds">A pure forward mapping, declared <see langword="static"/>.</param>
    public static OpaqueRenderBoundsContract FullInputs<TState>(
        TState state,
        Func<TState, IReadOnlyList<Rect>, Rect> transformBounds)
    {
        ArgumentNullException.ThrowIfNull(transformBounds);
        RenderDescriptionValidation.ValidatePureMetadataCallback(transformBounds, nameof(transformBounds));
        var binding = new CombineBoundsMapping<TState>(state, transformBounds, null);
        return new OpaqueRenderBoundsContract(
            OpaqueRenderBoundsKind.FullInputs,
            binding.TransformBounds,
            null,
            transformBounds);
    }

    /// <summary>Holds one recording's state so the combining mappings themselves stay static.</summary>
    private sealed class CombineBoundsMapping<TState>(
        TState state,
        Func<TState, IReadOnlyList<Rect>, Rect> transformBounds,
        Func<TState, Rect, IReadOnlyList<Rect>, IReadOnlyList<Rect>>? getRequiredInputBounds)
    {
        public Rect TransformBounds(IReadOnlyList<Rect> inputs) => transformBounds(state, inputs);

        public IReadOnlyList<Rect> GetRequiredInputBounds(Rect output, IReadOnlyList<Rect> inputs)
            => getRequiredInputBounds!(state, output, inputs);
    }

    internal OpaqueRenderBoundsKind Kind { get; }

    internal object StructuralIdentity { get; }

    internal Rect TransformBounds(IReadOnlyList<Rect> inputBounds)
    {
        ArgumentNullException.ThrowIfNull(inputBounds);
        ValidateRectangles(inputBounds, nameof(inputBounds));

        Rect result = Kind switch
        {
            OpaqueRenderBoundsKind.Source when inputBounds.Count == 0 => _sourceBounds,
            OpaqueRenderBoundsKind.Source => throw new InvalidOperationException(
                "A source bounds contract cannot receive input bounds."),
            OpaqueRenderBoundsKind.Map when inputBounds.Count == 1 => _mapBounds.TransformBounds(inputBounds[0]),
            OpaqueRenderBoundsKind.Map => throw new InvalidOperationException(
                "A map bounds contract requires exactly one input bound."),
            OpaqueRenderBoundsKind.Combine or OpaqueRenderBoundsKind.FullInputs => _transformBounds!(inputBounds),
            _ => throw new InvalidOperationException("The opaque render bounds contract is invalid."),
        };

        RenderRectValidation.ThrowIfInvalidResult(
            result,
            "The opaque render bounds forward mapping returned an invalid rectangle.");
        return result;
    }

    internal IReadOnlyList<Rect> GetRequiredInputBounds(
        Rect requestedOutputBounds,
        IReadOnlyList<Rect> inputBounds)
    {
        RenderRectValidation.ThrowIfInvalidInput(requestedOutputBounds, nameof(requestedOutputBounds));
        ArgumentNullException.ThrowIfNull(inputBounds);
        ValidateRectangles(inputBounds, nameof(inputBounds));

        if (Kind == OpaqueRenderBoundsKind.Source)
        {
            if (inputBounds.Count != 0)
                throw new InvalidOperationException("A source bounds contract cannot receive input bounds.");

            return Array.Empty<Rect>();
        }

        bool emptyRequirement = requestedOutputBounds.Width == 0 || requestedOutputBounds.Height == 0;
        IReadOnlyList<Rect> result;
        if (Kind == OpaqueRenderBoundsKind.Map)
        {
            if (inputBounds.Count != 1)
                throw new InvalidOperationException("A map bounds contract requires exactly one input bound.");

            Rect required = emptyRequirement
                ? Rect.Empty
                : _mapBounds.RequiresFullInput
                    ? inputBounds[0]
                    : _mapBounds.GetRequiredInputBounds(requestedOutputBounds);
            result = [required];
        }
        else if (Kind == OpaqueRenderBoundsKind.FullInputs)
        {
            result = emptyRequirement
                ? Enumerable.Repeat(Rect.Empty, inputBounds.Count).ToArray()
                : inputBounds.ToArray();
        }
        else
        {
            result = _getRequiredInputBounds!(requestedOutputBounds, inputBounds)
                ?? throw new InvalidOperationException("The opaque render bounds backward mapping returned null.");
        }

        if (result.Count != inputBounds.Count)
        {
            throw new InvalidOperationException(
                "The opaque render bounds backward mapping must return exactly one rectangle per input.");
        }

        ValidateResultRectangles(result);
        return result is ReadOnlyCollection<Rect> ? result : Array.AsReadOnly(result.ToArray());
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

    private static void ValidateResultRectangles(IReadOnlyList<Rect> values)
    {
        for (int index = 0; index < values.Count; index++)
        {
            if (!RenderRectValidation.IsFiniteNonNegative(values[index]))
            {
                throw new InvalidOperationException(
                    $"The opaque render bounds backward mapping returned an invalid rectangle at index {index}.");
            }
        }
    }
}
