namespace Beutl.Graphics.Rendering;

/// <summary>
/// Declares how a one-input operation carries a resolved output demand back to its input.
/// </summary>
/// <remarks>
/// Demand travels the opposite way from supply: a consumer asks for a density, and every operation between
/// it and a source decides what density that source has to produce. An operation that resamples its input —
/// a shader that enlarges what it samples, for instance — needs its input at a different density than it is
/// itself asked for, and only the operation knows the factor. Leaving demand unchanged is correct for an
/// operation that consumes its input at the density its own consumer asked for; for one that enlarges, it
/// lets an unbounded or vector input rasterize below the density the enlargement consumes, and the result is
/// blurred by exactly the enlargement factor. <see langword="default"/> is <see cref="Unchanged"/>.
/// </remarks>
public readonly struct RenderInputDemandContract
{
    private readonly Func<int, EffectiveScale, EffectiveScale>? _map;
    private readonly object? _structuralIdentity;

    private RenderInputDemandContract(
        Func<int, EffectiveScale, EffectiveScale> map,
        object structuralIdentity)
    {
        _map = map;
        _structuralIdentity = structuralIdentity;
    }

    /// <summary>Gets the contract that passes a resolved output demand to the input untouched.</summary>
    public static RenderInputDemandContract Unchanged => default;

    /// <summary>
    /// Creates a contract that maps a resolved output demand to the input demand that satisfies it.
    /// </summary>
    /// <param name="map">
    /// A pure metadata callback that maps a concrete output demand to the concrete input demand. It must
    /// return a finite positive density; the engine bounds the result by the request ceiling. It may be
    /// evaluated again during graph-wide metadata resolution, so it must remain deterministic and
    /// side-effect-free.
    /// </param>
    /// <returns>A declarative backward-demand mapping contract.</returns>
    public static RenderInputDemandContract MapOutputDemandToInput(
        Func<EffectiveScale, EffectiveScale> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        RenderDescriptionValidation.ValidatePureMetadataCallback(map, nameof(map));
        return new RenderInputDemandContract((_, demand) => map(demand), map);
    }

    /// <summary>
    /// Creates a contract that maps a resolved output demand to the demand on each input separately.
    /// </summary>
    /// <param name="map">
    /// A pure metadata callback that maps an input's zero-based index and the concrete output demand to that
    /// input's concrete demand. It must return a finite positive density for every index; the engine bounds
    /// each result by the request ceiling. It may be evaluated again during graph-wide metadata resolution, so
    /// it must remain deterministic and side-effect-free.
    /// </param>
    /// <returns>A declarative per-input backward-demand mapping contract.</returns>
    /// <remarks>
    /// This is what a many-input operation needs when it resamples its inputs asymmetrically — enlarging one
    /// while passing another through. A single map cannot express that, and leaving demand unchanged lets the
    /// enlarged input materialize below the density the enlargement consumes.
    /// </remarks>
    public static RenderInputDemandContract MapOutputDemandPerInput(
        Func<int, EffectiveScale, EffectiveScale> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        RenderDescriptionValidation.ValidatePureMetadataCallback(map, nameof(map));
        return new RenderInputDemandContract(map, map);
    }

    /// <summary>
    /// Creates a contract whose demand mapping reads call-owned state instead of closing over it.
    /// </summary>
    /// <typeparam name="TState">The immutable state the mapping reads.</typeparam>
    /// <param name="state">The per-recording values the mapping needs, which are request data.</param>
    /// <param name="map">A pure demand mapping, declared <see langword="static"/>.</param>
    public static RenderInputDemandContract MapOutputDemandToInput<TState>(
        TState state,
        Func<TState, EffectiveScale, EffectiveScale> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        RenderDescriptionValidation.ValidatePureMetadataCallback(map, nameof(map));
        var binding = new DemandMapping<TState>(state, map);
        return new RenderInputDemandContract(binding.Map, map);
    }

    /// <summary>
    /// Creates a per-input contract whose demand mapping reads call-owned state instead of closing over it.
    /// </summary>
    /// <typeparam name="TState">The immutable state the mapping reads.</typeparam>
    /// <param name="state">The per-recording values the mapping needs, which are request data.</param>
    /// <param name="map">A pure per-input demand mapping, declared <see langword="static"/>.</param>
    public static RenderInputDemandContract MapOutputDemandPerInput<TState>(
        TState state,
        Func<TState, int, EffectiveScale, EffectiveScale> map)
    {
        ArgumentNullException.ThrowIfNull(map);
        RenderDescriptionValidation.ValidatePureMetadataCallback(map, nameof(map));
        var binding = new PerInputDemandMapping<TState>(state, map);
        return new RenderInputDemandContract(binding.Map, map);
    }

    internal bool IsUnchanged => _map is null;

    internal object StructuralIdentity => _structuralIdentity ?? nameof(Unchanged);

    internal EffectiveScale Resolve(int inputIndex, EffectiveScale outputDemand)
    {
        if (outputDemand.IsUnbounded)
            throw new ArgumentException("Output demand must be concrete.", nameof(outputDemand));
        if (_map is null)
            return outputDemand;

        EffectiveScale mapped = _map(inputIndex, outputDemand);
        if (mapped.IsUnbounded)
        {
            throw new InvalidOperationException(
                "An output-demand mapping must return a concrete positive density.");
        }

        return EffectiveScale.At(mapped.Value);
    }

    /// <summary>Holds one recording's state so the mapping itself stays static.</summary>
    private sealed class DemandMapping<TState>(
        TState state,
        Func<TState, EffectiveScale, EffectiveScale> map)
    {
        public EffectiveScale Map(int inputIndex, EffectiveScale demand) => map(state, demand);
    }

    /// <summary>Holds one recording's state so the per-input mapping itself stays static.</summary>
    private sealed class PerInputDemandMapping<TState>(
        TState state,
        Func<TState, int, EffectiveScale, EffectiveScale> map)
    {
        public EffectiveScale Map(int inputIndex, EffectiveScale demand) => map(state, inputIndex, demand);
    }
}
