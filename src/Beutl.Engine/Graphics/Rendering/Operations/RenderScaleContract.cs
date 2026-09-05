using System.Collections.ObjectModel;

namespace Beutl.Graphics.Rendering;

public readonly struct RenderScaleContract
{
    private readonly RenderScaleContractKind _kind;
    private readonly Func<RenderScaleContext, float>? _resolve;
    private readonly Func<EffectiveScale, EffectiveScale>? _mapInputSupply;
    private readonly Func<EffectiveScale, EffectiveScale>? _mapOutputDemandToInput;
    private readonly object? _structuralIdentity;

    private RenderScaleContract(RenderScaleContractKind kind)
    {
        _kind = kind;
        _resolve = null;
        _mapInputSupply = null;
        _mapOutputDemandToInput = null;
        _structuralIdentity = kind;
    }

    private RenderScaleContract(Func<RenderScaleContext, float> resolve, object structuralIdentity)
    {
        _kind = RenderScaleContractKind.Custom;
        _resolve = resolve;
        _mapInputSupply = null;
        _mapOutputDemandToInput = null;
        _structuralIdentity = structuralIdentity;
    }

    private RenderScaleContract(
        Func<EffectiveScale, EffectiveScale> mapInputSupply,
        object structuralIdentity)
    {
        _kind = RenderScaleContractKind.MapInputSupply;
        _resolve = null;
        _mapInputSupply = mapInputSupply;
        _mapOutputDemandToInput = null;
        _structuralIdentity = new RenderScaleContractStructuralIdentity(_kind, structuralIdentity);
    }

    private RenderScaleContract(
        Func<EffectiveScale, EffectiveScale> mapInputSupply,
        Func<EffectiveScale, EffectiveScale> mapOutputDemandToInput,
        object structuralIdentity)
    {
        _kind = RenderScaleContractKind.MapInputSupply;
        _resolve = null;
        _mapInputSupply = mapInputSupply;
        _mapOutputDemandToInput = mapOutputDemandToInput;
        _structuralIdentity = new RenderScaleContractStructuralIdentity(_kind, structuralIdentity);
    }

    public static RenderScaleContract Vector { get; } = new(RenderScaleContractKind.Vector);

    public static RenderScaleContract PreserveInputSupply { get; } = new(RenderScaleContractKind.PreserveInputSupply);

    public static RenderScaleContract MaterializeAtWorkingScale { get; } =
        new(RenderScaleContractKind.MaterializeAtWorkingScale);

    /// <summary>
    /// Maps both directions of the density relationship of an element-wise one-input operation: the resolved
    /// input supply forward to the output supply, and the resolved output demand backward to the input demand.
    /// </summary>
    /// <param name="map">
    /// A pure metadata callback that maps the corresponding input supply to the output supply.
    /// The callback may return <see cref="EffectiveScale.Unbounded"/>.
    /// </param>
    /// <param name="mapOutputDemandToInput">
    /// A pure metadata callback that maps a concrete output demand to the concrete input demand that satisfies
    /// it. It must return a finite positive density; the engine bounds the result by the request ceiling.
    /// </param>
    /// <returns>A declarative bidirectional one-input density mapping contract.</returns>
    /// <remarks>
    /// This is the complete form and the right default for a one-input density map. An operation that enlarges
    /// its input lowers its output supply and raises its input demand, so a purely forward map would let an
    /// unbounded input rasterize below the density the enlargement consumes.
    /// Both callbacks may be evaluated again during graph-wide metadata resolution, so they must remain
    /// deterministic and side-effect-free. The backward map is not derived from the forward one: the forward
    /// map may collapse to <see cref="EffectiveScale.Unbounded"/> and need not be invertible.
    /// </remarks>
    public static RenderScaleContract MapInputSupply(
        Func<EffectiveScale, EffectiveScale> map,
        Func<EffectiveScale, EffectiveScale> mapOutputDemandToInput)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(mapOutputDemandToInput);
        RenderDescriptionValidation.ValidatePureMetadataCallback(map, nameof(map));
        RenderDescriptionValidation.ValidatePureMetadataCallback(
            mapOutputDemandToInput,
            nameof(mapOutputDemandToInput));
        return new RenderScaleContract(
            map,
            mapOutputDemandToInput,
            new RenderScaleBidirectionalMappingStructuralIdentity(
                RenderDescriptionValidation.StructuralIdentityOf(map),
                RenderDescriptionValidation.StructuralIdentityOf(mapOutputDemandToInput)));
    }

    /// <summary>
    /// Maps the resolved supply metadata of an element-wise one-input operation that consumes its input at the
    /// density its own consumer demands, so backward demand passes through unchanged.
    /// </summary>
    /// <param name="map">
    /// A pure metadata callback that maps the corresponding input supply to the output supply.
    /// The callback may return <see cref="EffectiveScale.Unbounded"/>.
    /// </param>
    /// <returns>A declarative forward-only one-input supply mapping contract.</returns>
    /// <remarks>
    /// The unchanged demand is the precondition, not a degraded default: it is what a supply map that reports a
    /// different density without resampling, or one that collapses to <see cref="EffectiveScale.Unbounded"/>,
    /// actually needs. An operation that resamples — an enlargement, a reduction — must use
    /// <see cref="MapInputSupply"/> instead, because leaving demand unchanged lets an unbounded input
    /// materialize below the density the operation consumes and blurs the result by the resampling factor.
    /// The callback may be evaluated again during graph-wide metadata resolution when an upstream fragment has
    /// symbolic recording metadata, so it must remain deterministic and side-effect-free.
    /// </remarks>
    public static RenderScaleContract MapInputSupplyPreservingDemand(
        Func<EffectiveScale, EffectiveScale> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        RenderDescriptionValidation.ValidatePureMetadataCallback(map, nameof(map));
        return new RenderScaleContract(map, RenderDescriptionValidation.StructuralIdentityOf(map));
    }

    /// <summary>
    /// Maps input supply with a forward map that reads call-owned state instead of closing over it.
    /// </summary>
    /// <typeparam name="TState">The immutable state the map reads.</typeparam>
    /// <param name="state">The per-recording values the map needs, which are request data.</param>
    /// <param name="map">A pure forward supply map, declared <see langword="static"/>.</param>
    public static RenderScaleContract MapInputSupplyPreservingDemand<TState>(
        TState state,
        Func<TState, EffectiveScale, EffectiveScale> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        RenderDescriptionValidation.ValidatePureMetadataCallback(map, nameof(map));
        var binding = new ScaleMapping<TState>(state, map, null);
        return new RenderScaleContract(
            binding.MapSupply,
            RenderDescriptionValidation.StructuralIdentityOf(map));
    }

    /// <summary>
    /// Resolves this operation's own concrete supply density from its inputs, output bounds, and the request's
    /// output scale and ceiling.
    /// </summary>
    /// <param name="resolve">
    /// A pure metadata callback returning a finite positive density. A throw or an invalid result fails the
    /// recording rather than being sanitized to a fallback.
    /// </param>
    /// <returns>A custom supply-resolving contract.</returns>
    /// <remarks>
    /// A custom resolver declares no backward map, and none can be attached to one: an output demand reaches
    /// this operation's inputs unchanged. That is correct only when this operation consumes its inputs at the
    /// density its own consumer demands. A one-input operation that resamples must instead use
    /// <see cref="MapInputSupply"/>, whose second callback carries the demand back; declaring the density here
    /// rather than there lets an unbounded input materialize below the density this operation consumes.
    /// </remarks>
    public static RenderScaleContract Custom(
        Func<RenderScaleContext, float> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        RenderDescriptionValidation.ValidatePureMetadataCallback(resolve, nameof(resolve));
        return new RenderScaleContract(
            resolve,
            RenderDescriptionValidation.StructuralIdentityOf(resolve));
    }

    /// <summary>
    /// Maps input supply and backward demand with callbacks that read call-owned state.
    /// </summary>
    /// <typeparam name="TState">The immutable state the maps read.</typeparam>
    /// <param name="state">The per-recording values the maps need, which are request data.</param>
    /// <param name="map">A pure forward supply map, declared <see langword="static"/>.</param>
    /// <param name="mapOutputDemandToInput">A pure backward demand map, declared the same way.</param>
    public static RenderScaleContract MapInputSupply<TState>(
        TState state,
        Func<TState, EffectiveScale, EffectiveScale> map,
        Func<TState, EffectiveScale, EffectiveScale> mapOutputDemandToInput)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(mapOutputDemandToInput);
        RenderDescriptionValidation.ValidatePureMetadataCallback(map, nameof(map));
        RenderDescriptionValidation.ValidatePureMetadataCallback(
            mapOutputDemandToInput,
            nameof(mapOutputDemandToInput));
        var binding = new ScaleMapping<TState>(state, map, mapOutputDemandToInput);
        return new RenderScaleContract(
            binding.MapSupply,
            binding.MapDemand,
            new RenderScaleBidirectionalMappingStructuralIdentity(
                RenderDescriptionValidation.StructuralIdentityOf(map),
                RenderDescriptionValidation.StructuralIdentityOf(mapOutputDemandToInput)));
    }

    /// <summary>
    /// Resolves this operation's supply density from a resolver that reads call-owned state.
    /// </summary>
    /// <typeparam name="TState">The immutable state the resolver reads.</typeparam>
    /// <param name="state">The per-recording values the resolver needs, which are request data.</param>
    /// <param name="resolve">A pure resolver, declared <see langword="static"/>.</param>
    public static RenderScaleContract Custom<TState>(
        TState state,
        Func<TState, RenderScaleContext, float> resolve)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        RenderDescriptionValidation.ValidatePureMetadataCallback(resolve, nameof(resolve));
        var binding = new ScaleResolver<TState>(state, resolve);
        return new RenderScaleContract(
            binding.Resolve,
            RenderDescriptionValidation.StructuralIdentityOf(resolve));
    }

    /// <summary>Holds one recording's state so the density maps themselves stay static.</summary>
    private sealed class ScaleMapping<TState>(
        TState state,
        Func<TState, EffectiveScale, EffectiveScale> map,
        Func<TState, EffectiveScale, EffectiveScale>? mapOutputDemandToInput)
    {
        public EffectiveScale MapSupply(EffectiveScale supply) => map(state, supply);

        public EffectiveScale MapDemand(EffectiveScale demand) => mapOutputDemandToInput!(state, demand);
    }

    /// <summary>Holds one recording's state so the resolver itself stays static.</summary>
    private sealed class ScaleResolver<TState>(
        TState state,
        Func<TState, RenderScaleContext, float> resolve)
    {
        public float Resolve(RenderScaleContext context) => resolve(state, context);
    }

    internal RenderScaleContractKind Kind => _kind;

    /// <summary>
    /// Gets whether this contract declares no supply density of its own, so its output resolves to
    /// <see cref="EffectiveScale.Unbounded"/> and adopts whatever density its consumer renders at.
    /// </summary>
    /// <remarks>
    /// <see cref="PreserveInputSupply"/> and the supply-mapping factories can also resolve to
    /// <see cref="EffectiveScale.Unbounded"/>, but only for a one-input map, whose supply is its input's rather
    /// than the consumer's. Every other kind resolves to a concrete positive density.
    /// </remarks>
    internal bool DeclaresNoSupplyDensity => _kind == RenderScaleContractKind.Vector;

    internal object StructuralIdentity
    {
        get
        {
            ThrowIfNotInitialized();
            return _structuralIdentity!;
        }
    }

    internal EffectiveScale Resolve(
        IReadOnlyList<EffectiveScale> inputSupplies,
        Rect outputBounds,
        float outputScale,
        float maxWorkingScale)
    {
        ThrowIfNotInitialized();
        ArgumentNullException.ThrowIfNull(inputSupplies);
        RenderRectValidation.ThrowIfInvalidInput(outputBounds, nameof(outputBounds));
        if (!float.IsFinite(outputScale) || outputScale <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(outputScale), outputScale, "Output scale must be positive and finite.");
        }

        float ceiling = RenderScaleUtilities.SanitizeMaxWorkingScale(maxWorkingScale);
        if (_kind == RenderScaleContractKind.Vector)
            return EffectiveScale.Unbounded;

        if (_kind == RenderScaleContractKind.PreserveInputSupply)
        {
            if (inputSupplies.Count != 1)
            {
                throw new InvalidOperationException(
                    "PreserveInputSupply requires exactly one corresponding input supply.");
            }

            return inputSupplies[0];
        }

        float resolved;
        if (_kind == RenderScaleContractKind.MapInputSupply)
        {
            if (inputSupplies.Count != 1)
            {
                throw new InvalidOperationException(
                    "MapInputSupply and MapInputSupplyPreservingDemand require exactly one corresponding input supply.");
            }

            EffectiveScale mapped = _mapInputSupply!(inputSupplies[0]);
            if (mapped.IsUnbounded)
                return EffectiveScale.Unbounded;

            resolved = EffectiveScale.At(mapped.Value).Value;
            resolved = MathF.Min(resolved, ceiling);
        }
        else if (_kind == RenderScaleContractKind.MaterializeAtWorkingScale)
        {
            resolved = RenderScaleUtilities.ResolveWorkingScale(inputSupplies.ToArray(), outputScale, ceiling);
        }
        else
        {
            resolved = _resolve!(new RenderScaleContext(
                inputSupplies is ReadOnlyCollection<EffectiveScale>
                    ? inputSupplies
                    : Array.AsReadOnly(inputSupplies.ToArray()),
                outputBounds,
                outputScale,
                ceiling));
            if (!float.IsFinite(resolved) || resolved <= 0)
            {
                throw new InvalidOperationException(
                    "A custom render scale resolver must return a positive finite value.");
            }

            resolved = MathF.Min(resolved, ceiling);
        }

        resolved = RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(outputBounds, resolved);
        if (!float.IsFinite(resolved) || resolved <= 0)
        {
            throw new InvalidOperationException(
                "The resolved render scale cannot produce a positive finite backing density.");
        }

        return EffectiveScale.At(resolved);
    }

    internal EffectiveScale MapOutputDemandToInput(EffectiveScale outputDemand)
    {
        ThrowIfNotInitialized();
        if (outputDemand.IsUnbounded)
            throw new ArgumentException("Output demand must be concrete.", nameof(outputDemand));

        EffectiveScale mapped = _kind == RenderScaleContractKind.MapInputSupply
                                && _mapOutputDemandToInput is not null
            ? _mapOutputDemandToInput(outputDemand)
            : outputDemand;
        if (mapped.IsUnbounded)
        {
            throw new InvalidOperationException(
                "An output-demand mapping must return a concrete positive density.");
        }

        return EffectiveScale.At(mapped.Value);
    }

    internal void ThrowIfUninitialized(string parameterName)
    {
        if (_kind == RenderScaleContractKind.Uninitialized || _structuralIdentity is null)
        {
            throw new ArgumentException(
                "default(RenderScaleContract) is uninitialized; use a named or custom contract.",
                parameterName);
        }
    }

    internal void ThrowIfIncompatible(OpaqueRenderTopology topology, string parameterName)
    {
        ThrowIfUninitialized(parameterName);
        if ((_kind is RenderScaleContractKind.PreserveInputSupply or RenderScaleContractKind.MapInputSupply)
            && topology != OpaqueRenderTopology.Map)
        {
            throw new ArgumentException(
                "A supply-preserving or supply-mapping contract is valid only for an element-wise one-input opaque map.",
                parameterName);
        }
    }

    private void ThrowIfNotInitialized()
    {
        if (_kind == RenderScaleContractKind.Uninitialized || _structuralIdentity is null)
        {
            throw new InvalidOperationException(
                "default(RenderScaleContract) is uninitialized; use a named or custom contract.");
        }
    }
}
