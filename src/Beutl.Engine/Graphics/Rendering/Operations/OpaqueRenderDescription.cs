using System.Collections.ObjectModel;
using System.Reflection;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Declares whether an opaque description's pixels depend on where the composition-device pixel grid falls.
/// </summary>
/// <remarks>
/// The renderer reuses a cached output only when every value that shaped its pixels is part of the cache
/// identity. Device-grid phase — the sub-pixel offset between the description's own coordinate space and the
/// pixel centres it writes to — is one such value, and no bounds, density, or author-supplied field carries it.
/// </remarks>
public enum RenderDeviceGridSensitivity : byte
{
    /// <summary>
    /// The output is unchanged by a sub-pixel shift of the device grid, so it may be cached and reused across
    /// device-grid phase changes and across a remapping replay.
    /// </summary>
    Insensitive,

    /// <summary>
    /// The output is a function of the device-grid phase, so a sub-pixel phase change or a remapping replay
    /// ancestor produces different pixels than the cached output.
    /// </summary>
    /// <remarks>
    /// Declare this for anything computed from where the pixel centres fall rather than resampled from a
    /// stored raster. Analytic anti-aliased coverage — glyph rasterization, signed-distance-field text — is
    /// one such source, and so are screen-space dithering, ordered noise, and pixel-grid overlays, which
    /// compute no coverage at all yet still change with the phase.
    /// </remarks>
    PhaseDependent,
}

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

    internal object GetStructuralIdentity(OpaqueRenderTopology topology)
        => new OpaqueRenderStructuralIdentity(
            topology,
            DefinitionFingerprint,
            DeviceGridSensitivity,
            BackendBoundary,
            HasDirectReplayMaterializationContract,
            DirectReplayAtExactIntegerReduction,
            SupportsDirectDstOut);

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
    /// Every pixel-affecting value the callback reads. It belongs here rather than in a capture, so that the
    /// plan stays keyed by the callback alone; when it changes, the owning node reports the change through
    /// <see cref="RenderNode.HasChanges"/>.
    /// </param>
    /// <param name="execute">
    /// A non-capturing callback. Declare it <see langword="static"/>: a capture would let a per-frame value
    /// shape the output without reaching <paramref name="state"/>, and is rejected.
    /// </param>
    /// <param name="deviceGridSensitivity">
    /// The declared dependency of the produced pixels on the device-grid phase. The default states that the
    /// output is unchanged by a sub-pixel shift of the grid, which lets the renderer cache and resample it.
    /// </param>
    /// <param name="inputDemand">
    /// What density each input has to reach for this operation's own resolved output demand. Only a combine
    /// or an expand may declare one, and it is what an operation that resamples its inputs asymmetrically
    /// needs: without it every input is asked for the unchanged output demand, so an unbounded input feeding
    /// an enlargement materializes below the density that enlargement consumes.
    /// </param>
    /// <param name="slots">
    /// The resource slots this operation declares. <paramref name="resources"/> must bind every one of them
    /// exactly once and is reordered into this list's order, so the order the caller wrote the bindings in
    /// never reaches the recorded operation. Omitting the list declares no slots rather than skipping that
    /// check, so binding a resource without declaring its slot is an error.
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
        IEnumerable<RenderResourceBinding>? resources = null,
        RenderInputDemandContract inputDemand = default,
        IEnumerable<RenderResourceSlot>? slots = null)
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
            RenderDescriptionValidation.BindDeclaredSlots(
                slots,
                resources,
                nameof(slots),
                nameof(resources)),
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
        IEnumerable<RenderResourceBinding>? resources = null)
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
        IEnumerable<RenderResourceBinding>? resources,
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
        IEnumerable<RenderResourceBinding>? resources = null)
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
            RenderDescriptionValidation.CopyResourceBindings(resources, nameof(resources)),
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
        IEnumerable<RenderResource>? resources = null)
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
        IEnumerable<RenderResource>? resources)
    {
        IReadOnlyList<RenderResource> copy =
            RenderDescriptionValidation.CopyResources(resources, nameof(resources));
        return copy
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

internal sealed class EngineDirectRenderSession
{
    private readonly RenderExecutionSessionToken _token;
    private readonly IReadOnlyList<RenderExecutionInput> _inputs;

    internal EngineDirectRenderSession(
        RenderExecutionSessionToken token,
        ImmediateCanvas canvas,
        IReadOnlyList<RenderExecutionInput> inputs)
    {
        _token = token;
        Canvas = canvas;
        _inputs = inputs;
    }

    internal ImmediateCanvas Canvas { get; }

    internal RenderExecutionSessionToken Token => _token;

    internal IReadOnlyList<RenderExecutionInput> Inputs
    {
        get { _token.ThrowIfInactive(); return _inputs; }
    }
}

internal enum RenderBackendBoundary : byte
{
    None,
    Graphics3D,
}

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
        StructuralIdentity = new OpaqueRenderBoundsStructuralIdentity(Kind, null, null, null);
    }

    private OpaqueRenderBoundsContract(RenderBoundsContract mapBounds)
    {
        Kind = OpaqueRenderBoundsKind.Map;
        _mapBounds = mapBounds;
        StructuralIdentity = new OpaqueRenderBoundsStructuralIdentity(
            Kind,
            mapBounds.StructuralIdentity,
            null,
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
            backward is null ? null : RenderDescriptionValidation.StructuralIdentityOf(backward),
            null);
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

public readonly struct RenderHitTestContract
{
    private readonly RenderHitTestContractKind _kind;
    private readonly Func<RenderHitTestContext, Point, bool>? _hitTest;
    private readonly object? _structuralIdentity;

    private RenderHitTestContract(RenderHitTestContractKind kind, object structuralIdentity)
    {
        _kind = kind;
        _hitTest = null;
        _structuralIdentity = structuralIdentity;
    }

    private RenderHitTestContract(
        Func<RenderHitTestContext, Point, bool> hitTest,
        object structuralIdentity)
    {
        _kind = RenderHitTestContractKind.Custom;
        _hitTest = hitTest;
        _structuralIdentity = structuralIdentity;
    }

    public static RenderHitTestContract None { get; } = new(
        RenderHitTestContractKind.None,
        RenderHitTestContractKind.None);

    public static RenderHitTestContract OutputBounds { get; } = new(
        RenderHitTestContractKind.OutputBounds,
        RenderHitTestContractKind.OutputBounds);

    public static RenderHitTestContract AnyInput { get; } = new(
        RenderHitTestContractKind.AnyInput,
        RenderHitTestContractKind.AnyInput);

    public static RenderHitTestContract Custom(
        Func<RenderHitTestContext, Point, bool> hitTest)
    {
        ArgumentNullException.ThrowIfNull(hitTest);
        RenderDescriptionValidation.ValidatePureMetadataCallback(hitTest, nameof(hitTest));
        return new RenderHitTestContract(
            hitTest,
            RenderDescriptionValidation.StructuralIdentityOf(hitTest));
    }

    /// <summary>
    /// Creates a hit test that reads call-owned state instead of closing over it.
    /// </summary>
    /// <typeparam name="TState">The immutable state the test reads.</typeparam>
    /// <param name="state">
    /// The per-recording values the test needs. They are request data, not plan identity: a recording that
    /// changes only this reruns the compiled plan rather than compiling a second one.
    /// </param>
    /// <param name="hitTest">
    /// The pure test. Declare it <see langword="static"/>; the plan is keyed by which callback it is, and only
    /// a static callback is the same delegate on every frame.
    /// </param>
    public static RenderHitTestContract Custom<TState>(
        TState state,
        Func<TState, RenderHitTestContext, Point, bool> hitTest)
    {
        ArgumentNullException.ThrowIfNull(hitTest);
        RenderDescriptionValidation.ValidatePureMetadataCallback(hitTest, nameof(hitTest));
        var binding = new HitTestBinding<TState>(state, hitTest);
        return new RenderHitTestContract(
            binding.HitTest,
            RenderDescriptionValidation.StructuralIdentityOf(hitTest));
    }

    /// <summary>
    /// Creates a hit test that reads the resource a call bound to <paramref name="slot"/>.
    /// </summary>
    /// <typeparam name="T">The raw resource type the slot addresses.</typeparam>
    /// <param name="slot">A slot the owning description declares.</param>
    /// <param name="hitTest">
    /// The pure test, given the bound resource. It must not capture a resource of its own; the slot is
    /// resolved against the bindings of the description being tested, so one hit test can be reused across
    /// recordings that bind different resources.
    /// </param>
    public static RenderHitTestContract FromSlot<T>(
        RenderResourceSlot<T> slot,
        Func<T, Point, bool> hitTest)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(hitTest);
        RenderDescriptionValidation.ValidatePureMetadataCallback(hitTest, nameof(hitTest));
        return new RenderHitTestContract(
            (context, point) => context.UseResource(slot, value => hitTest(value, point)),
            RenderDescriptionValidation.StructuralIdentityOf(hitTest));
    }

    /// <summary>
    /// Creates a hit test that reads the resource a call bound to <paramref name="slot"/> and also
    /// consults the operation's output bounds and inputs.
    /// </summary>
    /// <typeparam name="T">The raw resource type the slot addresses.</typeparam>
    /// <param name="slot">A slot the owning description declares.</param>
    /// <param name="hitTest">
    /// The pure test, given the bound resource and the hit-test context. It must not capture a resource
    /// of its own.
    /// </param>
    public static RenderHitTestContract FromSlot<T>(
        RenderResourceSlot<T> slot,
        Func<T, RenderHitTestContext, Point, bool> hitTest)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(hitTest);
        RenderDescriptionValidation.ValidatePureMetadataCallback(hitTest, nameof(hitTest));
        return new RenderHitTestContract(
            (context, point) => context.UseResource(slot, value => hitTest(value, context, point)),
            RenderDescriptionValidation.StructuralIdentityOf(hitTest));
    }

    internal static RenderHitTestContract FromResource<T>(
        RenderResource<T> resource,
        Func<T, Point, bool> hitTest)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(hitTest);
        return new RenderHitTestContract(
            (_, point) => resource.Registry.Use(resource, value => hitTest(value, point)),
            RenderDescriptionValidation.StructuralIdentityOf(hitTest));
    }

    internal static RenderHitTestContract FromResource<T>(
        RenderResource<T> resource,
        Func<T, RenderHitTestContext, Point, bool> hitTest)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(hitTest);
        return new RenderHitTestContract(
            (context, point) => resource.Registry.Use(
                resource,
                value => hitTest(value, context, point)),
            RenderDescriptionValidation.StructuralIdentityOf(hitTest));
    }

    internal static RenderHitTestContract FromResource<T, TState>(
        RenderResource<T> resource,
        TState state,
        Func<T, TState, Point, bool> hitTest)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(hitTest);
        RenderDescriptionValidation.ValidatePureMetadataCallback(hitTest, nameof(hitTest));
        var binding = new ResourceHitTestBinding<T, TState>(resource, state, hitTest);
        return new RenderHitTestContract(
            binding.HitTest,
            RenderDescriptionValidation.StructuralIdentityOf(hitTest));
    }

    /// <summary>Holds one recording's state so the test itself stays static.</summary>
    private sealed class HitTestBinding<TState>(
        TState state,
        Func<TState, RenderHitTestContext, Point, bool> hitTest)
    {
        public bool HitTest(RenderHitTestContext context, Point point) => hitTest(state, context, point);
    }

    /// <summary>Holds one recording's resource and state so the test itself stays static.</summary>
    private sealed class ResourceHitTestBinding<T, TState>(
        RenderResource<T> resource,
        TState state,
        Func<T, TState, Point, bool> hitTest)
        where T : class
    {
        public bool HitTest(RenderHitTestContext context, Point point)
            => resource.Registry.Use(resource, value => hitTest(value, state, point));
    }

    internal RenderHitTestContractKind Kind => _kind;

    internal object StructuralIdentity
    {
        get
        {
            ThrowIfNotInitialized();
            return _structuralIdentity!;
        }
    }

    internal bool Evaluate(
        Rect outputBounds,
        IReadOnlyList<RenderHitTestInput> inputs,
        IReadOnlyList<RenderResourceBinding> resources,
        Point point)
    {
        ThrowIfNotInitialized();
        RenderRectValidation.ThrowIfInvalidInput(outputBounds, nameof(outputBounds));
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(resources);

        return _kind switch
        {
            RenderHitTestContractKind.None => false,
            RenderHitTestContractKind.OutputBounds => outputBounds.Contains(point),
            RenderHitTestContractKind.AnyInput => inputs.Any(input => input.HitTest(point)),
            RenderHitTestContractKind.Custom =>
                _hitTest!(new RenderHitTestContext(outputBounds, inputs, resources), point),
            _ => throw new InvalidOperationException("The hit-test contract is invalid."),
        };
    }

    internal void ThrowIfUninitialized(string parameterName)
    {
        if (_kind == RenderHitTestContractKind.Uninitialized || _structuralIdentity is null)
        {
            throw new ArgumentException(
                "default(RenderHitTestContract) is uninitialized; use None, OutputBounds, AnyInput, or Custom.",
                parameterName);
        }
    }

    private void ThrowIfNotInitialized()
    {
        if (_kind == RenderHitTestContractKind.Uninitialized || _structuralIdentity is null)
        {
            throw new InvalidOperationException(
                "default(RenderHitTestContract) is uninitialized; use None, OutputBounds, AnyInput, or Custom.");
        }
    }
}

public sealed class RenderHitTestContext
{
    private readonly IReadOnlyList<RenderResourceBinding> _resources;

    internal RenderHitTestContext(
        Rect outputBounds,
        IReadOnlyList<RenderHitTestInput> inputs,
        IReadOnlyList<RenderResourceBinding> resources)
    {
        OutputBounds = outputBounds;
        Inputs = inputs is ReadOnlyCollection<RenderHitTestInput>
            ? inputs
            : Array.AsReadOnly(inputs.ToArray());
        _resources = resources;
    }

    public Rect OutputBounds { get; }

    public IReadOnlyList<RenderHitTestInput> Inputs { get; }

    /// <summary>
    /// Reads the resource that the call being hit-tested bound to <paramref name="slot"/>.
    /// </summary>
    /// <typeparam name="T">The raw resource type the slot addresses.</typeparam>
    /// <typeparam name="TResult">The value the reader produces.</typeparam>
    /// <param name="slot">A slot the owning description declares.</param>
    /// <param name="use">Reads the bound resource. The raw value must not outlive this call.</param>
    /// <exception cref="KeyNotFoundException">The call bound no resource to that slot.</exception>
    public TResult UseResource<T, TResult>(RenderResourceSlot<T> slot, Func<T, TResult> use)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(use);

        foreach (RenderResourceBinding binding in _resources)
        {
            if (ReferenceEquals(binding.Slot, slot))
            {
                var resource = (RenderResource<T>)binding.Resource;
                return resource.Registry.Use(resource, use);
            }
        }

        throw new KeyNotFoundException(
            "No resource was bound to the requested slot for this hit test.");
    }
}

public readonly struct RenderHitTestInput
{
    private readonly Func<Point, bool>? _hitTest;

    internal RenderHitTestInput(Rect bounds, Func<Point, bool> hitTest)
    {
        RenderRectValidation.ThrowIfInvalidInput(bounds, nameof(bounds));
        ArgumentNullException.ThrowIfNull(hitTest);
        Bounds = bounds;
        _hitTest = hitTest;
    }

    public Rect Bounds { get; }

    public bool HitTest(Point point)
    {
        if (_hitTest is null)
            throw new InvalidOperationException("The hit-test input is uninitialized.");

        return _hitTest(point);
    }
}

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

public readonly record struct RenderScaleContext(
    IReadOnlyList<EffectiveScale> InputSupplies,
    Rect OutputBounds,
    float OutputScale,
    float MaxWorkingScale);

public sealed class OpaqueRenderSession
{
    private readonly RenderExecutionSessionToken _token;
    private readonly IReadOnlyList<RenderResourceBinding> _resourceBindings;
    private readonly IReadOnlyList<RenderResource> _resources;
    private readonly Func<OpaqueRenderSession, Rect, float?, OpaqueRenderOutput> _createOutput;
    private readonly Action<OpaqueRenderOutput> _publish;
    private readonly IReadOnlyList<RenderExecutionInput> _inputs;
    private readonly IReadOnlyList<RenderExecutionInputRange> _inputRanges;
    private readonly Rect _outputBounds;
    private readonly Rect _requiredRegion;
    private readonly PixelRect _deviceBounds;
    private readonly float _outputScale;
    private readonly float _workingScale;
    private readonly float _maxWorkingScale;
    private readonly RenderIntent _intent;
    private readonly RenderRequestPurpose _purpose;

    internal OpaqueRenderSession(
        RenderExecutionSessionToken token,
        IReadOnlyList<RenderExecutionInput> inputs,
        IReadOnlyList<RenderExecutionInputRange> inputRanges,
        Rect outputBounds,
        Rect requiredRegion,
        PixelRect deviceBounds,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        IReadOnlyList<RenderResourceBinding> resources,
        Func<OpaqueRenderSession, Rect, float?, OpaqueRenderOutput> createOutput,
        Action<OpaqueRenderOutput> publish)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(inputRanges);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(createOutput);
        ArgumentNullException.ThrowIfNull(publish);
        _token = token;
        _inputs = Array.AsReadOnly(inputs.ToArray());
        _inputRanges = RenderExecutionInputRange.CopyAndValidate(
            _inputs,
            inputRanges,
            nameof(inputRanges));
        _outputBounds = outputBounds;
        _requiredRegion = requiredRegion;
        _deviceBounds = deviceBounds;
        _outputScale = outputScale;
        _workingScale = workingScale;
        _maxWorkingScale = maxWorkingScale;
        _intent = intent;
        _purpose = purpose;
        _resourceBindings = resources;
        _resources = resources.SelectToArray(static binding => binding.Resource);
        _createOutput = createOutput;
        _publish = publish;
    }

    internal RenderExecutionSessionToken Token => _token;

    public IReadOnlyList<RenderExecutionInput> Inputs
    {
        get { _token.ThrowIfInactive(); return _inputs; }
    }

    /// <summary>
    /// Gets one stable flattened-input range per authored input handle, including zero-length ranges for handles
    /// that produced no runtime values.
    /// </summary>
    public IReadOnlyList<RenderExecutionInputRange> InputRanges
    {
        get { _token.ThrowIfInactive(); return _inputRanges; }
    }

    public Rect OutputBounds
    {
        get { _token.ThrowIfInactive(); return _outputBounds; }
    }

    public Rect RequiredRegion
    {
        get { _token.ThrowIfInactive(); return _requiredRegion; }
    }

    public PixelRect DeviceBounds
    {
        get { _token.ThrowIfInactive(); return _deviceBounds; }
    }

    public PixelSize DeviceSize
    {
        get { _token.ThrowIfInactive(); return _deviceBounds.Size; }
    }

    public float OutputScale
    {
        get { _token.ThrowIfInactive(); return _outputScale; }
    }

    public float WorkingScale
    {
        get { _token.ThrowIfInactive(); return _workingScale; }
    }

    public float MaxWorkingScale
    {
        get { _token.ThrowIfInactive(); return _maxWorkingScale; }
    }

    public RenderIntent Intent
    {
        get { _token.ThrowIfInactive(); return _intent; }
    }

    public RenderRequestPurpose Purpose
    {
        get { _token.ThrowIfInactive(); return _purpose; }
    }

    /// <summary>Creates an unpublished output within the declared bounds.</summary>
    /// <param name="logicalBounds">The finite non-empty logical output bounds.</param>
    /// <param name="density">
    /// The optional finite positive density for this output. <see langword="null"/> uses
    /// <see cref="WorkingScale"/>. The executor clamps either value to engine allocation limits.
    /// </param>
    public OpaqueRenderOutput CreateOutput(Rect logicalBounds, float? density = null)
    {
        _token.ThrowIfInactive();
        RenderDescriptionValidation.ThrowIfFiniteNonEmpty(logicalBounds, nameof(logicalBounds));
        if (density is { } value && (!float.IsFinite(value) || value <= 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(density),
                density,
                "An opaque output density must be finite and positive.");
        }
        if (!RenderDescriptionValidation.Contains(_outputBounds, logicalBounds))
        {
            throw new ArgumentException("An opaque output must be contained by the declared output bounds.", nameof(logicalBounds));
        }

        return _createOutput(this, logicalBounds, density);
    }

    public void Publish(OpaqueRenderOutput output)
    {
        _token.ThrowIfInactive();
        ArgumentNullException.ThrowIfNull(output);
        output.Publish(this, _publish);
    }

    /// <summary>Uses the resource bound to a declared slot.</summary>
    public void UseResource<T>(RenderResourceSlot<T> slot, Action<T> use)
        where T : class
    {
        _token.UseResource(slot, _resourceBindings, use);
    }

    internal void UseResource<T>(RenderResource<T> resource, Action<T> use)
        where T : class
    {
        _token.UseResource(resource, _resources, use);
    }

    internal void UseNestedTarget(
        RenderResource<NestedRenderTargetBinding> resource,
        Action<NestedRenderTargetImage> use)
    {
        ArgumentNullException.ThrowIfNull(use);
        _token.UseResource(
            resource,
            _resources,
            binding => binding.UseImage(_token, use));
    }
}

public sealed class OpaqueRenderOutput : IDisposable
{
    private readonly RenderExecutionSessionToken _token;
    private readonly OpaqueRenderSession _owner;
    private readonly Rect _allocationBounds;
    private readonly EffectiveScale _effectiveScale;
    private readonly RenderCallbackCanvas _canvas;
    private readonly Action<OpaqueRenderOutput>? _release;
    private Rect _bounds;
    private OpaqueRenderOutputState _state;

    internal OpaqueRenderOutput(
        RenderExecutionSessionToken token,
        OpaqueRenderSession owner,
        Rect bounds,
        EffectiveScale effectiveScale,
        RenderCallbackCanvas canvas,
        Action<OpaqueRenderOutput>? release = null)
    {
        _token = token;
        _owner = owner;
        _allocationBounds = bounds;
        _bounds = bounds;
        _effectiveScale = effectiveScale;
        _canvas = canvas;
        _release = release;
    }

    public Rect Bounds
    {
        get { ThrowIfUnavailable(); return _bounds; }
    }

    public EffectiveScale EffectiveScale
    {
        get { ThrowIfUnavailable(); return _effectiveScale; }
    }

    public RenderCallbackCanvas Canvas
    {
        get { ThrowIfUnavailable(); return _canvas; }
    }

    public void SetOutputBounds(Rect logicalBounds)
    {
        ThrowIfUnavailable();
        RenderRectValidation.ThrowIfInvalidInput(logicalBounds, nameof(logicalBounds));
        if (!RenderDescriptionValidation.Contains(_allocationBounds, logicalBounds))
        {
            throw new ArgumentException(
                "Output bounds may only shrink within the allocated output bounds.",
                nameof(logicalBounds));
        }

        _bounds = logicalBounds;
    }

    public void Discard()
    {
        ThrowIfUnavailable();
        _state = OpaqueRenderOutputState.Discarded;
        _release?.Invoke(this);
    }

    public void Dispose()
    {
        _token.ThrowIfInactive();
        if (_state != OpaqueRenderOutputState.Active)
            return;

        _state = OpaqueRenderOutputState.Disposed;
        _release?.Invoke(this);
    }

    internal void Publish(OpaqueRenderSession owner, Action<OpaqueRenderOutput> publish)
    {
        ThrowIfUnavailable();
        if (!ReferenceEquals(owner, _owner))
            throw new InvalidOperationException("An opaque output belongs to a different execution session.");

        publish(this);
        _state = OpaqueRenderOutputState.Published;
    }

    private void ThrowIfUnavailable()
    {
        _token.ThrowIfInactive();
        if (_state != OpaqueRenderOutputState.Active)
            throw new InvalidOperationException("The opaque output lease is no longer active.");
    }
}

internal enum OpaqueRenderTopology : byte
{
    Source,
    Map,
    Combine,
    Expand,
}

internal enum OpaqueRenderBoundsKind : byte
{
    Source,
    Map,
    Combine,
    FullInputs,
}

internal enum RenderHitTestContractKind : byte
{
    Uninitialized,
    None,
    OutputBounds,
    AnyInput,
    Custom,
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

internal readonly record struct OpaqueRenderBoundsStructuralIdentity(
    OpaqueRenderBoundsKind Kind,
    object? ForwardIdentity,
    object? BackwardIdentity,
    object? ExplicitKey);

internal readonly record struct RenderScaleContractStructuralIdentity(
    RenderScaleContractKind Kind,
    object CallbackIdentity);

internal readonly record struct RenderScaleBidirectionalMappingStructuralIdentity(
    MethodInfo SupplyMap,
    MethodInfo DemandMap);

internal readonly record struct OpaqueRenderStructuralIdentity(
    OpaqueRenderTopology Topology,
    object DescriptionKey,
    RenderDeviceGridSensitivity DeviceGridSensitivity,
    RenderBackendBoundary BackendBoundary,
    bool HasDirectReplayMaterializationContract,
    bool DirectReplayAtExactIntegerReduction,
    bool SupportsDirectDstOut);

internal sealed record EngineOpaqueDefinition(
    RenderBackendBoundary BackendBoundary,
    object Execute,
    object? DirectReplay,
    bool DirectReplayAtExactIntegerReduction);

internal static class RenderDescriptionValidation
{
    /// <summary>
    /// Binds an execution callback to the state one description carries.
    /// </summary>
    public static RenderExecutionChannel<TSession> CreateStateChannel<TSession, TState>(
        TState state,
        Action<TSession, TState> execute,
        string stateParameterName,
        string executeParameterName)
        where TState : notnull
    {
        ValidateStatePassingCallback(state, execute, stateParameterName, executeParameterName);
        return RenderExecutionChannel<TSession>.FromState(state, execute);
    }

    /// <summary>
    /// Enforces the state-passing rule: every per-recording value reaches the callback through its call state.
    /// </summary>
    public static void ValidateStatePassingCallback<TState>(
        TState state,
        Delegate execute,
        string stateParameterName,
        string executeParameterName)
        where TState : notnull
    {
        ArgumentNullException.ThrowIfNull(execute, executeParameterName);

        // typeof(TState).IsValueType is a JIT-time constant, so a value-typed state never reaches the
        // object-taking checks below and is never boxed on the recording path.
        if (!typeof(TState).IsValueType)
        {
            if (state is null)
                throw new ArgumentNullException(stateParameterName);

            ThrowIfExecutionFacadeIdentity(state, stateParameterName);
        }

    }

    public static RenderExecutionChannel<TSession> CreateRequestLocalChannel<TSession>(
        Action<TSession> execute,
        string executeParameterName)
    {
        ArgumentNullException.ThrowIfNull(execute, executeParameterName);
        return RenderExecutionChannel<TSession>.RequestLocal(execute);
    }

    /// <summary>
    /// A recorded query region is the whole region the operation reports to Measure and ROI, so a hit outside it
    /// is a hit no consumer sized itself for. A zero-area region reports nothing, yet every hit-testing kind can
    /// still answer true somewhere: <see cref="RenderHitTestContractKind.OutputBounds"/> because
    /// <see cref="Rect.Contains"/> is edge-inclusive and an empty rectangle still holds its own origin,
    /// <see cref="RenderHitTestContractKind.AnyInput"/> because it delegates to input regions the operation never
    /// declared, and <see cref="RenderHitTestContractKind.Custom"/> because the callback answers for any point at
    /// all. Only <see cref="RenderHitTestContractKind.None"/> is confined to an empty region.
    /// </summary>
    public static void ThrowIfQueryContributionIncoherent(
        Rect queryBounds,
        RenderHitTestContract hitTest,
        string parameterName)
    {
        if ((queryBounds.Width > 0 && queryBounds.Height > 0)
            || hitTest.Kind == RenderHitTestContractKind.None)
        {
            return;
        }

        throw new ArgumentException(
            "A zero-area queryBounds contributes no query region, so the hit-test contract must be "
            + "RenderHitTestContract.None.",
            parameterName);
    }

    public static void ValidatePureMetadataCallback(Delegate callback, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(callback);
        object? target = callback.Target;
        if (target is null)
            return;

        ThrowIfExecutionFacadeIdentity(target, parameterName);
        RenderIdentityKeyValidator.ThrowIfInvalid(target, parameterName);
    }

    /// <summary>What a metadata callback contributes to the structural identity of the operation holding it.</summary>
    /// <remarks>
    /// <para>
    /// The method, not the delegate. A structural identity says which plan a recording can be served by, and a
    /// plan is the shape of the work: what a callback answers is request data the plan is re-run over, which is
    /// why a recording that only resizes reuses its plan rather than compiling a second one. A callback the
    /// engine holds to being a pure function of its arguments therefore contributes which declaration it is and
    /// nothing about the instance it reads, so two nodes of one type that read different values of their own
    /// share the plan the way two calls of one static callback already do.
    /// </para>
    /// <para>
    /// This is confined to the callbacks <see cref="ValidatePureMetadataCallback"/> gates. An execution
    /// callback carries no such promise, so <see cref="StructuralIdentityOfExecution"/> reads its target
    /// before deciding the same way.
    /// </para>
    /// <para>
    /// <see cref="Delegate.Method"/> is cached by the runtime, so reading it allocates nothing.
    /// </para>
    /// </remarks>
    public static MethodInfo StructuralIdentityOf(Delegate callback) => callback.Method;

    /// <summary>What an execution callback contributes to the structural identity of the operation holding it.</summary>
    /// <remarks>
    /// <para>
    /// The method when the callback's target is the <see cref="RenderNode"/> that declared it, and the
    /// delegate otherwise. A metadata callback can take the method unconditionally because the engine holds
    /// it to being a pure function of its arguments and can therefore ignore what it reads. No such promise
    /// covers an execution callback, so what it closed over has to keep separating it - which is what the
    /// request-local overloads rest on: their callback closes over a recording, arrives with a compiler
    /// display class as its target, and keeps the fresh per-recording identity that bars it from a later
    /// request's cache lookup.
    /// </para>
    /// <para>
    /// A node is not something the callback closed over. It is the one target
    /// <see cref="RenderIdentityKeyValidator"/> admits, it is re-read on every recording rather than
    /// snapshotted at one, and what it holds is governed by <see cref="RenderNode.MarkChanged"/> - the same
    /// contract that already governs the state a non-capturing callback is handed, and the one BESG005
    /// reports an unmarked write against. So it belongs on the request-data side of the split the plan key
    /// draws: two nodes of one type share the shape of the work and re-run it over their own values, exactly
    /// as they already do for the metadata callbacks those nodes declare.
    /// </para>
    /// <para>
    /// A static callback is unaffected either way: the compiler caches one delegate per declaration, so the
    /// delegate was already as stable as the method.
    /// </para>
    /// </remarks>
    public static object StructuralIdentityOfExecution(Delegate callback)
        => callback.Target is RenderNode ? callback.Method : callback;

    /// <summary>
    /// How many slots a declaration may hold before the duplicate check stops being a linear scan.
    /// </summary>
    /// <remarks>
    /// A node declares its slot list once and hands it over on every recording, so this runs on the render
    /// path. At the sizes a declaration actually reaches - two is the widest any built-in node declares -
    /// building a hash set to reject a repeat costs several times what comparing the handful of references
    /// already copied does, and the copy can be sized from the declaration instead of grown into. Past this
    /// width the quadratic scan is the more expensive of the two and the set is built after all.
    /// </remarks>
    private const int LinearSlotScanLimit = 8;

    public static IReadOnlyList<RenderResource> CopyResources(
        IEnumerable<RenderResource>? resources,
        string parameterName)
    {
        if (resources is null)
            return Array.Empty<RenderResource>();

        var result = new List<RenderResource>();
        foreach (RenderResource? resource in resources)
        {
            if (resource is null)
                throw new ArgumentException("A declared render resource cannot be null.", parameterName);
            if (resource.RegistrationState == RenderResourceRegistrationState.Released)
                throw new ArgumentException("A released render resource cannot be declared.", parameterName);

            result.Add(resource);
        }

        return result.Count == 0 ? Array.Empty<RenderResource>() : result.AsReadOnly();
    }

    public static IReadOnlyList<RenderResourceBinding> CopyResourceBindings(
        IEnumerable<RenderResourceBinding>? resources,
        string parameterName)
    {
        // An empty sequence has nothing to check, and the recording paths reach this once per operation per
        // frame with no resources at all - by far the common case - so the working set is built only once
        // there is something to put in it.
        if (resources is null or IReadOnlyCollection<RenderResourceBinding> { Count: 0 })
            return Array.Empty<RenderResourceBinding>();

        if (resources is IReadOnlyList<RenderResourceBinding> { Count: <= LinearSlotScanLimit } declared)
            return CopyShortResourceBindings(declared, parameterName);

        var slots = new HashSet<RenderResourceSlot>(ReferenceEqualityComparer.Instance);
        var result = new List<RenderResourceBinding>();
        foreach (RenderResourceBinding? binding in resources)
        {
            if (binding is null)
                throw new ArgumentException("A declared render resource binding cannot be null.", parameterName);
            if (!slots.Add(binding.Slot))
                throw new ArgumentException("A render resource slot cannot be bound more than once.", parameterName);
            ThrowIfUndeclarable(binding.Resource, parameterName);
            result.Add(binding);
        }

        return result.Count == 0 ? Array.Empty<RenderResourceBinding>() : result.AsReadOnly();
    }

    /// <inheritdoc cref="CopyShortResourceSlots" path="/remarks"/>
    private static IReadOnlyList<RenderResourceBinding> CopyShortResourceBindings(
        IReadOnlyList<RenderResourceBinding> bindings,
        string parameterName)
    {
        var copy = new RenderResourceBinding[bindings.Count];
        for (int index = 0; index < copy.Length; index++)
        {
            RenderResourceBinding binding = bindings[index];
            if (binding is null)
                throw new ArgumentException("A declared render resource binding cannot be null.", parameterName);

            for (int bound = 0; bound < index; bound++)
            {
                if (ReferenceEquals(copy[bound].Slot, binding.Slot))
                {
                    throw new ArgumentException(
                        "A render resource slot cannot be bound more than once.",
                        parameterName);
                }
            }

            ThrowIfUndeclarable(binding.Resource, parameterName);
            copy[index] = binding;
        }

        return Array.AsReadOnly(copy);
    }

    private static IReadOnlyList<RenderResourceSlot> CopyResourceSlots(
        IEnumerable<RenderResourceSlot>? slots,
        string parameterName)
    {
        if (slots is null or IReadOnlyCollection<RenderResourceSlot> { Count: 0 })
            return Array.Empty<RenderResourceSlot>();

        if (slots is IReadOnlyList<RenderResourceSlot> { Count: <= LinearSlotScanLimit } declared)
            return CopyShortResourceSlots(declared, parameterName);

        var seen = new HashSet<RenderResourceSlot>(ReferenceEqualityComparer.Instance);
        var result = new List<RenderResourceSlot>();
        foreach (RenderResourceSlot? slot in slots)
        {
            if (slot is null)
                throw new ArgumentException("A render resource slot cannot be null.", parameterName);
            if (!seen.Add(slot))
                throw new ArgumentException("A render resource slot cannot be declared more than once.", parameterName);
            result.Add(slot);
        }

        return result.Count == 0 ? Array.Empty<RenderResourceSlot>() : result.AsReadOnly();
    }

    /// <remarks>
    /// The copy is what the scan reads, so a caller handing over an array it mutates afterwards cannot
    /// change either the check or the list this returns.
    /// </remarks>
    private static IReadOnlyList<RenderResourceSlot> CopyShortResourceSlots(
        IReadOnlyList<RenderResourceSlot> slots,
        string parameterName)
    {
        var copy = new RenderResourceSlot[slots.Count];
        for (int index = 0; index < copy.Length; index++)
        {
            RenderResourceSlot slot = slots[index];
            if (slot is null)
                throw new ArgumentException("A render resource slot cannot be null.", parameterName);

            for (int declared = 0; declared < index; declared++)
            {
                if (ReferenceEquals(copy[declared], slot))
                {
                    throw new ArgumentException(
                        "A render resource slot cannot be declared more than once.",
                        parameterName);
                }
            }

            copy[index] = slot;
        }

        return Array.AsReadOnly(copy);
    }

    /// <summary>
    /// Puts already-copied bindings into declared-slot order, refusing a set that does not match.
    /// </summary>
    /// <remarks>
    /// The bindings arrive copied, so this neither re-enumerates them nor re-checks what the copy already
    /// refused. Which binding answers for a declared slot is found by scanning, which for the widths a
    /// declaration reaches is cheaper than the index built to avoid the scan; past
    /// <see cref="LinearSlotScanLimit"/> that reverses and the index is built.
    /// </remarks>
    private static IReadOnlyList<RenderResourceBinding> OrderByDeclaredSlots(
        IReadOnlyList<RenderResourceSlot> declaredSlots,
        IReadOnlyList<RenderResourceBinding> bindings,
        string parameterName)
    {
        if (declaredSlots.Count != bindings.Count)
        {
            throw new ArgumentException(
                "A render description must bind every resource slot it declares exactly once.",
                parameterName);
        }

        Dictionary<RenderResourceSlot, RenderResourceBinding>? bySlot = null;
        if (bindings.Count > LinearSlotScanLimit)
        {
            bySlot = new Dictionary<RenderResourceSlot, RenderResourceBinding>(
                bindings.Count,
                ReferenceEqualityComparer.Instance);
            foreach (RenderResourceBinding binding in bindings)
                bySlot.Add(binding.Slot, binding);
        }

        var ordered = new RenderResourceBinding[declaredSlots.Count];
        for (int index = 0; index < ordered.Length; index++)
        {
            RenderResourceSlot slot = declaredSlots[index];
            RenderResourceBinding? bound = bySlot is null
                ? FindBinding(bindings, slot)
                : bySlot.GetValueOrDefault(slot);
            if (bound is null)
            {
                throw new ArgumentException(
                    "A render description contains a resource slot it did not declare.",
                    parameterName);
            }

            ordered[index] = bound;
        }

        return Array.AsReadOnly(ordered);
    }

    private static RenderResourceBinding? FindBinding(
        IReadOnlyList<RenderResourceBinding> bindings,
        RenderResourceSlot slot)
    {
        for (int index = 0; index < bindings.Count; index++)
        {
            if (ReferenceEquals(bindings[index].Slot, slot))
                return bindings[index];
        }

        return null;
    }

    /// <summary>
    /// Applies a declared slot list to a factory that is handed bindings alone.
    /// </summary>
    /// <remarks>
    /// A bindings-only factory has no slot list of its own, so nothing there can tell a caller that bound one
    /// slot twice and another not at all. Passing the declared slots restores that check, and with it the
    /// normalization it performs: the returned bindings are in declared-slot order, so a structural identity
    /// built from them - <see cref="Beutl.Graphics.Effects.GeometryDescription"/>'s resource-type sequence
    /// among them - stops depending on the order the caller happened to write them in.
    /// <para>
    /// A <see langword="null"/> <paramref name="slots"/> declares none rather than opting out of the check,
    /// so an omitted slot list still reaches the same validation an empty one does. Bindings supplied against
    /// it are refused here: the recorded operation would otherwise carry resources in the order the caller
    /// wrote them, which is exactly the order dependence this normalization exists to remove.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<RenderResourceBinding> BindDeclaredSlots(
        IEnumerable<RenderResourceSlot>? slots,
        IEnumerable<RenderResourceBinding>? bindings,
        string slotsParameterName,
        string bindingsParameterName)
    {
        IReadOnlyList<RenderResourceSlot> declaredSlots = CopyResourceSlots(slots, slotsParameterName);

        // Copied once, before the count is read: a caller-supplied sequence may be a generator, so every
        // check below and the list this returns have to read one enumeration of it.
        IReadOnlyList<RenderResourceBinding> declaredBindings = CopyResourceBindings(
            bindings,
            bindingsParameterName);
        if (declaredSlots.Count == 0)
        {
            if (declaredBindings.Count > 0)
            {
                throw new ArgumentException(
                    "A render call that declares no resource slots cannot bind a resource. Declare the slots "
                    + "the bindings address, so each one is checked and ordered against that declaration.",
                    slotsParameterName);
            }

            // Declaring nothing and binding nothing is the default every recording path takes, and it is
            // already checked by the two counts above.
            return Array.Empty<RenderResourceBinding>();
        }

        return OrderByDeclaredSlots(declaredSlots, declaredBindings, bindingsParameterName);
    }

    public static void ThrowIfUndeclarable(RenderResource resource, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(resource, parameterName);
        if (resource.RegistrationState == RenderResourceRegistrationState.Released)
            throw new ArgumentException("A released render resource cannot be declared.", parameterName);
    }

    public static void ThrowIfFiniteNonEmpty(Rect bounds, string parameterName)
    {
        RenderRectValidation.ThrowIfInvalidInput(bounds, parameterName);
        if (bounds.Width == 0 || bounds.Height == 0)
            throw new ArgumentException("Bounds must be non-empty.", parameterName);
    }

    public static bool Contains(Rect outer, Rect inner)
        => inner.Left >= outer.Left
           && inner.Top >= outer.Top
           && inner.Right <= outer.Right
           && inner.Bottom <= outer.Bottom;

    private static void ThrowIfExecutionFacadeIdentity(object value, string parameterName)
    {
        if (value is RenderExecutionInput
            or RenderCallbackCanvas
            or OpaqueRenderSession
            or OpaqueRenderOutput
            or GeometrySession
            or ShaderExecutionContext
            or ShaderUniformWriter
            or ShaderResourceWriter
            or TargetScopeSession
            or TargetCommandSession
            or RawTargetScopeSession
            or RawTargetCommandSession)
        {
            throw new ArgumentException(
                "A persistent identity or pure metadata callback cannot retain an execution session or facade.",
                parameterName);
        }
    }
}
