using System.Collections.ObjectModel;
using System.Reflection;

namespace Beutl.Graphics.Rendering;

public sealed class OpaqueRenderDescription
{
    private static readonly ReadOnlyCollection<RenderInputReadback> s_noInputReadbacks =
        Array.AsReadOnly(Array.Empty<RenderInputReadback>());

    private readonly RenderExecutionChannel<OpaqueRenderSession> _execution;

    private OpaqueRenderDescription(
        RenderExecutionChannel<OpaqueRenderSession> execution,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderInputDemandContract inputDemand,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        object definitionFingerprint,
        IReadOnlyList<RenderInputReadback> inputReadbacks,
        IReadOnlyList<RenderResourceBinding> resources,
        RenderBackendBoundary backendBoundary,
        Action<EngineDirectRenderSession>? directReplay,
        bool supportsDirectDstOut,
        bool hasDirectReplayMaterializationContract = false,
        bool directReplayAtExactIntegerReduction = false)
    {
        _execution = execution;
        Bounds = bounds;
        HitTest = hitTest;
        ValueCardinality = valueCardinality;
        Scale = scale;
        InputDemand = inputDemand;
        DeviceGridSensitivity = deviceGridSensitivity;
        DefinitionFingerprint = definitionFingerprint;
        InputReadbacks = inputReadbacks;
        Resources = resources;
        BackendBoundary = backendBoundary;
        DirectReplay = directReplay;
        SupportsDirectDstOut = supportsDirectDstOut;
        HasDirectReplayMaterializationContract = hasDirectReplayMaterializationContract;
        DirectReplayAtExactIntegerReduction = directReplayAtExactIntegerReduction;
    }

    public OpaqueRenderBoundsContract Bounds { get; }

    public RenderHitTestContract HitTest { get; }

    public RenderValueCardinality ValueCardinality { get; }

    public RenderScaleContract Scale { get; }

    /// <summary>Gets the mapping from this operation's resolved output demand to the demand on each input.</summary>
    /// <remarks>
    /// Only a combine or an expand may declare one. A one-input map carries demand backwards through
    /// <see cref="RenderScaleContract.MapInputSupply"/> instead, and a source has no input to demand from.
    /// </remarks>
    public RenderInputDemandContract InputDemand { get; }

    /// <summary>Gets the declared dependency of this description's pixels on the device pixel grid.</summary>
    public RenderDeviceGridSensitivity DeviceGridSensitivity { get; }

    public IReadOnlyList<RenderInputReadback> InputReadbacks { get; }

    internal object DefinitionFingerprint { get; }

    public IReadOnlyList<RenderResourceBinding> Resources { get; }

    internal void Execute(OpaqueRenderSession session) => _execution.Invoke(session);

    internal RenderBackendBoundary BackendBoundary { get; }

    internal Action<EngineDirectRenderSession>? DirectReplay { get; }

    internal bool SupportsDirectDstOut { get; }

    internal bool HasDirectReplayMaterializationContract { get; }

    internal bool DirectReplayAtExactIntegerReduction { get; }

    internal void ThrowIfIncompatible(OpaqueRenderTopology topology, string parameterName)
    {
        Bounds.ThrowIfIncompatible(topology, parameterName);
        Scale.ThrowIfIncompatible(topology, parameterName);

        if (!InputDemand.IsUnchanged
            && topology is not (OpaqueRenderTopology.Combine or OpaqueRenderTopology.Expand))
        {
            throw new ArgumentException(
                "Only a combine or an expand declares a per-input demand mapping; a one-input map declares it "
                + "through its scale contract and a source has no input.",
                parameterName);
        }

        if (DirectReplay is not null
            && topology is not (OpaqueRenderTopology.Source or OpaqueRenderTopology.Combine))
        {
            throw new ArgumentException(
                "An engine direct-replay description can only be recorded as an opaque source or combine.",
                parameterName);
        }

        bool cardinalityValid = topology switch
        {
            OpaqueRenderTopology.Map =>
                ValueCardinality.Equals(RenderValueCardinality.Single)
                || ValueCardinality.Equals(RenderValueCardinality.ZeroOrOne),
            OpaqueRenderTopology.Combine => ValueCardinality.Maximum is <= 1,
            OpaqueRenderTopology.Source => ValueCardinality.Maximum is <= 1,
            OpaqueRenderTopology.Expand => true,
            _ => false,
        };
        if (!cardinalityValid)
        {
            throw new ArgumentException(
                $"The declared value cardinality is incompatible with {topology} topology.",
                parameterName);
        }

        if (topology == OpaqueRenderTopology.Source && HitTest.Kind == RenderHitTestContractKind.AnyInput)
        {
            throw new ArgumentException(
                "An opaque source has no logical inputs and cannot use AnyInput hit testing.",
                parameterName);
        }
    }

    internal OpaqueRenderDescription WithoutDirectReplay()
        => DirectReplay is null
            ? this
            : new OpaqueRenderDescription(
                _execution,
                Bounds,
                HitTest,
                ValueCardinality,
                Scale,
                InputDemand,
                DeviceGridSensitivity,
                DefinitionFingerprint,
                InputReadbacks,
                Resources,
                BackendBoundary,
                directReplay: null,
                supportsDirectDstOut: false,
                hasDirectReplayMaterializationContract: false,
                directReplayAtExactIntegerReduction: false);

    /// <param name="state">
    /// Immutable pixel-affecting state retained for execution.
    /// </param>
    /// <param name="execute">
    /// A static execution callback.
    /// </param>
    /// <param name="deviceGridSensitivity">
    /// Whether device-grid phase affects the produced pixels.
    /// </param>
    /// <param name="inputDemand">
    /// Per-input density required by a combine or expand for its resolved output demand.
    /// </param>
    public static OpaqueRenderDescription Create<TState>(
        TState state,
        Action<OpaqueRenderSession, TState> execute,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        IReadOnlyList<RenderResourceBinding>? resources = null,
        RenderInputDemandContract inputDemand = default)
        where TState : notnull
        => CreateCore(
            RenderDescriptionValidation.CreateStateChannel(
                state,
                execute,
                nameof(state),
                nameof(execute)),
            bounds,
            hitTest,
            valueCardinality,
            scale,
            deviceGridSensitivity,
            RenderDescriptionValidation.StructuralIdentityOfExecution(execute),
            inputReadbacks,
            resources,
            inputDemand);

    /// <summary>
    /// Creates an opaque description whose output can never satisfy a later request's cache lookup.
    /// </summary>
    /// <remarks>
    /// The opt-out for a callback whose pixel-affecting state cannot be expressed as copied, deeply immutable
    /// CPU state. The callback may capture, and the recorded output takes a fresh request-local identity every
    /// time.
    /// </remarks>
    internal static OpaqueRenderDescription CreateRequestLocal(
        Action<OpaqueRenderSession> execute,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.PhaseDependent,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        IReadOnlyList<RenderResourceBinding>? resources = null)
        => CreateCore(
            RenderDescriptionValidation.CreateRequestLocalChannel(execute, nameof(execute)),
            bounds,
            hitTest,
            valueCardinality,
            scale,
            deviceGridSensitivity,
            execute,
            inputReadbacks,
            resources);

    internal static OpaqueRenderDescription CreateCore(
        RenderExecutionChannel<OpaqueRenderSession> execution,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        object definitionFingerprint,
        IEnumerable<RenderInputReadback>? inputReadbacks,
        IReadOnlyList<RenderResourceBinding>? resources,
        RenderInputDemandContract inputDemand = default)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        valueCardinality.ThrowIfUninitialized(nameof(valueCardinality));
        scale.ThrowIfUninitialized(nameof(scale));
        ThrowIfUndefined(deviceGridSensitivity);

        ArgumentNullException.ThrowIfNull(definitionFingerprint);

        return new OpaqueRenderDescription(
            execution,
            bounds,
            hitTest,
            valueCardinality,
            scale,
            inputDemand,
            deviceGridSensitivity,
            definitionFingerprint,
            Array.AsReadOnly(CopyInputReadbacks(inputReadbacks)),
            RenderDescriptionValidation.CopyResourceBindings(resources, nameof(resources)),
            RenderBackendBoundary.None,
            directReplay: null,
            supportsDirectDstOut: false);
    }

    /// <summary>
    /// Creates an engine-owned drawable source whose identity is declared rather than derived from state.
    /// </summary>
    /// <remarks>
    /// The callback is assembled by a shared recorder helper and reaches request-scoped resources and a
    /// recorded paint plan, neither of which can be part of a persistent identity, so the declared identity is
    /// hand-verified against what the helper draws with. The shape is reachable from outside the engine through
    /// <see cref="RenderNodeContext"/>'s <c>PaintedSource</c>, whose caller-supplied draw callback is held
    /// purely by BESG003 rather than by the shape being unreachable.
    /// </remarks>
    /// <param name="resources">
    /// The fresh binding array assembled by <see cref="RenderNodeContext"/>, stored without another copy.
    /// </param>
    internal static OpaqueRenderDescription CreateEngineSource<TState>(
        TState state,
        Action<OpaqueRenderSession, TState> execute,
        Action<EngineDirectRenderSession, TState>? directReplay,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        bool directReplayAtExactIntegerReduction = false,
        bool supportsDirectDstOut = true,
        IReadOnlyList<RenderResourceBinding>? resources = null)
        where TState : notnull
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(bounds);
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        scale.ThrowIfUninitialized(nameof(scale));
        ThrowIfUndefined(deviceGridSensitivity);
        object definitionFingerprint = new EngineOpaqueDefinition(
            RenderBackendBoundary.None,
            RenderDescriptionValidation.StructuralIdentityOfExecution(execute),
            directReplay is null
                ? null
                : RenderDescriptionValidation.StructuralIdentityOfExecution(directReplay),
            directReplayAtExactIntegerReduction);
        Action<EngineDirectRenderSession>? boundDirectReplay = directReplay is null
            ? null
            : new EngineDirectSourceBinding<TState>(state, directReplay).Replay;

        return new OpaqueRenderDescription(
            RenderDescriptionValidation.CreateStateChannel(
                state,
                execute,
                nameof(state),
                nameof(execute)),
            bounds,
            hitTest,
            RenderValueCardinality.Single,
            scale,
            RenderInputDemandContract.Unchanged,
            deviceGridSensitivity,
            definitionFingerprint,
            s_noInputReadbacks,
            resources ?? Array.Empty<RenderResourceBinding>(),
            RenderBackendBoundary.None,
            boundDirectReplay,
            supportsDirectDstOut && directReplay is not null,
            hasDirectReplayMaterializationContract:
                directReplay is not null && scale.DeclaresNoSupplyDensity,
            directReplayAtExactIntegerReduction:
                directReplay is not null && directReplayAtExactIntegerReduction);
    }

    internal static OpaqueRenderDescription CreateBackendBoundary<TState>(
        RenderBackendBoundary backendBoundary,
        TState state,
        Action<OpaqueRenderSession, TState> execute,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        IReadOnlyList<RenderResource>? resources = null)
        where TState : notnull
    {
        if (backendBoundary == RenderBackendBoundary.None || !Enum.IsDefined(backendBoundary))
            throw new ArgumentOutOfRangeException(nameof(backendBoundary));
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(bounds);
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        valueCardinality.ThrowIfUninitialized(nameof(valueCardinality));
        scale.ThrowIfUninitialized(nameof(scale));
        ThrowIfUndefined(deviceGridSensitivity);
        object definitionFingerprint = new EngineOpaqueDefinition(
            backendBoundary,
            RenderDescriptionValidation.StructuralIdentityOfExecution(execute),
            DirectReplay: null,
            DirectReplayAtExactIntegerReduction: false);

        return new OpaqueRenderDescription(
            RenderDescriptionValidation.CreateStateChannel(
                state,
                execute,
                nameof(state),
                nameof(execute)),
            bounds,
            hitTest,
            valueCardinality,
            scale,
            RenderInputDemandContract.Unchanged,
            deviceGridSensitivity,
            definitionFingerprint,
            s_noInputReadbacks,
            BindInternalResources(resources),
            backendBoundary,
            directReplay: null,
            supportsDirectDstOut: false);
    }

    /// <summary>Binds an engine source's direct-replay callback to one recording's state.</summary>
    private sealed class EngineDirectSourceBinding<TState>(
        TState state,
        Action<EngineDirectRenderSession, TState> directReplay)
    {
        public void Replay(EngineDirectRenderSession session) => directReplay(session, state);
    }

    private static void ThrowIfUndefined(RenderDeviceGridSensitivity deviceGridSensitivity)
    {
        if (!Enum.IsDefined(deviceGridSensitivity))
            throw new ArgumentOutOfRangeException(nameof(deviceGridSensitivity));
    }

    private static IReadOnlyList<RenderResourceBinding> BindInternalResources(
        IReadOnlyList<RenderResource>? resources)
    {
        IReadOnlyList<RenderResource> declared = resources ?? Array.Empty<RenderResource>();
        RenderDescriptionValidation.ThrowIfResourcesUndeclarable(declared, nameof(resources));
        return declared
            .Select(static resource => RenderResourceBinding.CreateEngineBinding(resource))
            .ToArray();
    }

    internal IReadOnlyList<RenderInputReadback> ResolveInputReadbacks(
        int inputCount,
        string parameterName)
    {
        if (InputReadbacks.Count == 0)
            return Enumerable.Repeat(RenderInputReadback.None, inputCount).ToArray();
        if (InputReadbacks.Count != inputCount)
        {
            throw new ArgumentException(
                "The opaque-render input readback count must match the authored input count.",
                parameterName);
        }
        return InputReadbacks;
    }

    private static RenderInputReadback[] CopyInputReadbacks(
        IEnumerable<RenderInputReadback>? inputReadbacks)
    {
        if (inputReadbacks is null)
            return [];

        RenderInputReadback[] result = inputReadbacks.ToArray();
        foreach (RenderInputReadback inputReadback in result)
            inputReadback.ThrowIfUninitialized(nameof(inputReadbacks));
        return result;
    }
}

internal enum RenderScaleContractKind : byte
{
    Uninitialized,
    Vector,
    PreserveInputSupply,
    MapInputSupply,
    MaterializeAtWorkingScale,
    Custom,
}

internal enum OpaqueRenderOutputState : byte
{
    Active,
    Published,
    Discarded,
    Disposed,
}

internal readonly record struct RenderScaleContractStructuralIdentity(
    RenderScaleContractKind Kind,
    object CallbackIdentity);

internal readonly record struct RenderScaleBidirectionalMappingStructuralIdentity(
    MethodInfo SupplyMap,
    MethodInfo DemandMap);

internal sealed record EngineOpaqueDefinition(
    RenderBackendBoundary BackendBoundary,
    object Execute,
    object? DirectReplay,
    bool DirectReplayAtExactIntegerReduction);
