namespace Beutl.Graphics.Rendering.Requests;

internal sealed class RenderFragmentOutputIdentity : IEquatable<RenderFragmentOutputIdentity>
{
    private readonly RenderFragmentKind _kind;
    private readonly Rect _bounds;
    private readonly int? _scaleBits;
    private readonly int? _materializationScaleBits;
    private readonly RenderValueCardinality _cardinality;
    private readonly bool _contributes;
    private readonly object[] _runtimeComponents;
    private readonly RenderFragmentOutputIdentity[] _inputs;
    private readonly int _hash;

    private RenderFragmentOutputIdentity(
        RenderFragmentReference reference,
        EffectiveScale? materializationDemand,
        object[] runtimeComponents,
        RenderFragmentOutputIdentity[] inputs)
    {
        _kind = reference.Kind;
        _bounds = reference.Bounds;
        _scaleBits = reference.EffectiveScale.IsUnbounded
            ? null
            : BitConverter.SingleToInt32Bits(reference.EffectiveScale.Value);
        _materializationScaleBits = materializationDemand is { } demand
            ? BitConverter.SingleToInt32Bits(demand.Value)
            : null;
        _cardinality = reference.ValueCardinality;
        _contributes = reference.ContributesValuesToTarget;
        _runtimeComponents = runtimeComponents;
        _inputs = inputs;
        _hash = ComputeHash();
    }

    // Inputs are built and hashed first, so folding in their final hash keeps hashing linear in edges.
    private int ComputeHash()
    {
        var hash = new HashCode();
        hash.Add(_kind);
        hash.Add(_bounds);
        hash.Add(_scaleBits);
        hash.Add(_materializationScaleBits);
        hash.Add(_cardinality);
        hash.Add(_contributes);
        foreach (object component in _runtimeComponents)
            hash.Add(component);
        foreach (RenderFragmentOutputIdentity input in _inputs)
            hash.Add(input._hash);
        return hash.ToHashCode();
    }

    internal static RenderFragmentOutputIdentity Create(
        RenderFragmentReference reference,
        RenderRequestId graphRequestId,
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale>? materializationDemands,
        IDictionary<RenderFragmentReference, RenderFragmentOutputIdentity> memo,
        float outputScale,
        float maxWorkingScale)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(memo);
        return CreateCore(
            reference,
            graphRequestId,
            materializationDemands,
            memo,
            outputScale,
            maxWorkingScale);
    }

    public bool Equals(RenderFragmentOutputIdentity? other)
        => other is not null && EqualsCore(other, null);

    // Records pairs being proven so a shared subgraph is compared once, not once per path.
    private bool EqualsCore(
        RenderFragmentOutputIdentity other,
        HashSet<(RenderFragmentOutputIdentity Left, RenderFragmentOutputIdentity Right)>? proven)
    {
        if (ReferenceEquals(this, other))
            return true;

        if (_hash != other._hash
            || _kind != other._kind
            || !_bounds.Equals(other._bounds)
            || _scaleBits != other._scaleBits
            || _materializationScaleBits != other._materializationScaleBits
            || !_cardinality.Equals(other._cardinality)
            || _contributes != other._contributes
            || _runtimeComponents.Length != other._runtimeComponents.Length
            || _inputs.Length != other._inputs.Length)
        {
            return false;
        }

        for (int index = 0; index < _runtimeComponents.Length; index++)
        {
            if (!Equals(_runtimeComponents[index], other._runtimeComponents[index]))
                return false;
        }

        if (_inputs.Length == 0)
            return true;

        proven ??= new HashSet<(RenderFragmentOutputIdentity, RenderFragmentOutputIdentity)>();
        if (!proven.Add((this, other)))
            return true;

        for (int index = 0; index < _inputs.Length; index++)
        {
            if (!_inputs[index].EqualsCore(other._inputs[index], proven))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj)
        => obj is RenderFragmentOutputIdentity other && Equals(other);

    public override int GetHashCode() => _hash;

    private static RenderFragmentOutputIdentity CreateCore(
        RenderFragmentReference reference,
        RenderRequestId requestId,
        IReadOnlyDictionary<RenderFragmentReference, EffectiveScale>? materializationDemands,
        IDictionary<RenderFragmentReference, RenderFragmentOutputIdentity> memo,
        float outputScale,
        float maxWorkingScale)
    {
        if (memo.TryGetValue(reference, out RenderFragmentOutputIdentity? cached))
            return cached;

        RenderFragmentOutputIdentity[] inputs = reference.Inputs
            .Select(input => CreateCore(
                input,
                requestId,
                materializationDemands,
                memo,
                outputScale,
                maxWorkingScale))
            .ToArray();
        var components = new List<object>();
        AddRequestScopedComponents(
            reference,
            requestId,
            outputScale,
            maxWorkingScale,
            components);
        EffectiveScale? demand = materializationDemands?.TryGetValue(
            reference,
            out EffectiveScale selectedDemand) == true
            ? selectedDemand
            : null;
        var identity = new RenderFragmentOutputIdentity(
            reference,
            demand,
            components.ToArray(),
            inputs);
        memo.Add(reference, identity);
        return identity;
    }

    private static void AddRequestScopedComponents(
        RenderFragmentReference reference,
        RenderRequestId requestId,
        float outputScale,
        float maxWorkingScale,
        ICollection<object> components)
    {
        switch (reference.Payload)
        {
            case BuiltInBackdropCaptureRenderFragmentPayload capture:
                components.Add(capture.Description.SourceRegion);
                components.Add(capture.Description.Bounds);
                components.Add(RequestLocalIdentity(reference, requestId, "backdrop"));
                return;
            case RawTargetCommandRenderFragmentPayload:
                components.Add(RequestLocalIdentity(reference, requestId, "raw-target"));
                return;
            case ShaderRenderFragmentPayload { Description.HasExecutionContextBinder: true }:
                // The stage's own scale already answers the density it runs at. These two are the request
                // values a binder can read without either one moving that density, so nothing else in the
                // identity separates two requests whose binder deliberately paints them differently.
                components.Add(new RequestScaleRenderCacheIdentity(
                    BitConverter.SingleToInt32Bits(outputScale),
                    BitConverter.SingleToInt32Bits(maxWorkingScale)));
                return;
            default:
                return;
        }
    }

    private static object RequestLocalIdentity(
        RenderFragmentReference reference,
        RenderRequestId requestId,
        string role)
        => new RequestLocalRenderCacheIdentity(
            requestId.Value,
            reference.Id?.Value ?? 0,
            role);

    private sealed record RequestLocalRenderCacheIdentity(
        long RequestId,
        long FragmentId,
        string Role);

    /// <remarks>
    /// The scales are held as bits so two requests are only interchangeable when the binder would read the
    /// very same value, rather than whichever ones float equality happens to conflate.
    /// </remarks>
    private sealed record RequestScaleRenderCacheIdentity(
        int OutputScaleBits,
        int MaxWorkingScaleBits);

}
