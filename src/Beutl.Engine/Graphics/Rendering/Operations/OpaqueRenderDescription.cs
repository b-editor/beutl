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
/// pixel centres it writes to — is one such value, and no bounds, density, or runtime identity carries it.
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
    private readonly RenderExecutionChannel<OpaqueRenderSession> _execution;

    private OpaqueRenderDescription(
        RenderExecutionChannel<OpaqueRenderSession> execution,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        object structuralKey,
        IReadOnlyList<RenderInputReadback> inputReadbacks,
        IReadOnlyList<RenderResource> resources,
        RenderBackendBoundary backendBoundary,
        Action<EngineDirectRenderSession>? directReplay)
    {
        _execution = execution;
        RuntimeIdentity = RenderDescriptionValidation.ResolveRuntimeIdentity(execution);
        Bounds = bounds;
        HitTest = hitTest;
        ValueCardinality = valueCardinality;
        Scale = scale;
        DeviceGridSensitivity = deviceGridSensitivity;
        StructuralKey = structuralKey;
        InputReadbacks = inputReadbacks;
        Resources = resources;
        BackendBoundary = backendBoundary;
        DirectReplay = directReplay;
    }

    public OpaqueRenderBoundsContract Bounds { get; }

    public RenderHitTestContract HitTest { get; }

    public RenderValueCardinality ValueCardinality { get; }

    public RenderScaleContract Scale { get; }

    /// <summary>Gets the declared dependency of this description's pixels on the device pixel grid.</summary>
    public RenderDeviceGridSensitivity DeviceGridSensitivity { get; }

    public IReadOnlyList<RenderInputReadback> InputReadbacks { get; }

    public object StructuralKey { get; }

    public RenderRuntimeIdentity? RuntimeIdentity { get; }

    public IReadOnlyList<RenderResource> Resources { get; }

    internal void Execute(OpaqueRenderSession session) => _execution.Invoke(session);

    internal RenderBackendBoundary BackendBoundary { get; }

    internal Action<EngineDirectRenderSession>? DirectReplay { get; }

    internal void ThrowIfIncompatible(OpaqueRenderTopology topology, string parameterName)
    {
        Bounds.ThrowIfIncompatible(topology, parameterName);
        Scale.ThrowIfIncompatible(topology, parameterName);

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
            StructuralKey,
            DeviceGridSensitivity,
            BackendBoundary,
            DirectReplay is not null);

    internal OpaqueRenderDescription WithoutDirectReplay()
        => DirectReplay is null
            ? this
            : new OpaqueRenderDescription(
                _execution,
                Bounds,
                HitTest,
                ValueCardinality,
                Scale,
                DeviceGridSensitivity,
                StructuralKey,
                InputReadbacks,
                Resources,
                BackendBoundary,
                directReplay: null);

    /// <param name="state">
    /// Every pixel-affecting value the callback reads, and the complete output-cache runtime identity of the
    /// produced value. It must be a lightweight immutable CPU value.
    /// </param>
    /// <param name="execute">
    /// A non-capturing callback. Declare it <see langword="static"/>: a capture would let a per-frame value
    /// shape the output without reaching <paramref name="state"/>, and is rejected.
    /// </param>
    /// <param name="deviceGridSensitivity">
    /// The declared dependency of the produced pixels on the device-grid phase. The default states that the
    /// output is unchanged by a sub-pixel shift of the grid, which lets the renderer cache and resample it.
    /// </param>
    public static OpaqueRenderDescription Create<TState>(
        TState state,
        Action<OpaqueRenderSession, TState> execute,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.Insensitive,
        object? structuralKey = null,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        IEnumerable<RenderResource>? resources = null)
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
            structuralKey,
            inputReadbacks,
            resources);

    /// <summary>
    /// Creates an opaque description whose output can never satisfy a later request's cache lookup.
    /// </summary>
    /// <remarks>
    /// The opt-out for a callback whose pixel-affecting state cannot be expressed as a lightweight immutable
    /// key. The callback may capture, and the recorded output takes a fresh request-local identity every time.
    /// </remarks>
    public static OpaqueRenderDescription CreateRequestLocal(
        Action<OpaqueRenderSession> execute,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity = RenderDeviceGridSensitivity.Insensitive,
        object? structuralKey = null,
        IEnumerable<RenderInputReadback>? inputReadbacks = null,
        IEnumerable<RenderResource>? resources = null)
        => CreateCore(
            RenderDescriptionValidation.CreateRequestLocalChannel(execute, nameof(execute)),
            bounds,
            hitTest,
            valueCardinality,
            scale,
            deviceGridSensitivity,
            structuralKey,
            inputReadbacks,
            resources);

    private static OpaqueRenderDescription CreateCore(
        RenderExecutionChannel<OpaqueRenderSession> execution,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        object? structuralKey,
        IEnumerable<RenderInputReadback>? inputReadbacks,
        IEnumerable<RenderResource>? resources)
    {
        ArgumentNullException.ThrowIfNull(bounds);
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        valueCardinality.ThrowIfUninitialized(nameof(valueCardinality));
        scale.ThrowIfUninitialized(nameof(scale));
        ThrowIfUndefined(deviceGridSensitivity);

        object resolvedStructuralKey = RenderDescriptionValidation.ResolveStructuralKey(
            structuralKey,
            execution.Method,
            nameof(structuralKey));

        return new OpaqueRenderDescription(
            execution,
            bounds,
            hitTest,
            valueCardinality,
            scale,
            deviceGridSensitivity,
            resolvedStructuralKey,
            Array.AsReadOnly(CopyInputReadbacks(inputReadbacks)),
            RenderDescriptionValidation.CopyResources(resources, nameof(resources)),
            RenderBackendBoundary.None,
            directReplay: null);
    }

    /// <summary>
    /// Creates an engine-owned drawable source whose identity is declared rather than derived from state.
    /// </summary>
    /// <remarks>
    /// The callback is assembled by a shared recorder helper and reaches request-scoped resources and a
    /// recorded paint plan, neither of which can be part of a persistent identity, so the declared identity is
    /// hand-verified against what the helper draws with. Nothing outside the engine can reach this shape.
    /// </remarks>
    internal static OpaqueRenderDescription CreateEngineSource(
        Action<OpaqueRenderSession> execute,
        Action<EngineDirectRenderSession>? directReplay,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        object structuralKey,
        RenderRuntimeIdentity? runtimeIdentity,
        IEnumerable<RenderResource>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(bounds);
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        scale.ThrowIfUninitialized(nameof(scale));
        ThrowIfUndefined(deviceGridSensitivity);
        ArgumentNullException.ThrowIfNull(structuralKey);
        RenderIdentityKeyValidator.ThrowIfInvalid(structuralKey, nameof(structuralKey));

        return new OpaqueRenderDescription(
            RenderDescriptionValidation.CreateDeclaredIdentityChannel(
                execute,
                runtimeIdentity,
                nameof(execute),
                nameof(runtimeIdentity)),
            bounds,
            hitTest,
            RenderValueCardinality.Single,
            scale,
            deviceGridSensitivity,
            structuralKey,
            Array.AsReadOnly(Array.Empty<RenderInputReadback>()),
            RenderDescriptionValidation.CopyResources(resources, nameof(resources)),
            RenderBackendBoundary.None,
            directReplay);
    }

    internal static OpaqueRenderDescription CreateBackendBoundary(
        RenderBackendBoundary backendBoundary,
        Action<OpaqueRenderSession> execute,
        OpaqueRenderBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        RenderDeviceGridSensitivity deviceGridSensitivity,
        object structuralKey,
        RenderRuntimeIdentity runtimeIdentity,
        IEnumerable<RenderResource>? resources = null)
    {
        if (backendBoundary == RenderBackendBoundary.None || !Enum.IsDefined(backendBoundary))
            throw new ArgumentOutOfRangeException(nameof(backendBoundary));
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(bounds);
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        valueCardinality.ThrowIfUninitialized(nameof(valueCardinality));
        scale.ThrowIfUninitialized(nameof(scale));
        ThrowIfUndefined(deviceGridSensitivity);
        ArgumentNullException.ThrowIfNull(structuralKey);
        RenderIdentityKeyValidator.ThrowIfInvalid(structuralKey, nameof(structuralKey));

        return new OpaqueRenderDescription(
            RenderDescriptionValidation.CreateDeclaredIdentityChannel(
                execute,
                runtimeIdentity,
                nameof(execute),
                nameof(runtimeIdentity)),
            bounds,
            hitTest,
            valueCardinality,
            scale,
            deviceGridSensitivity,
            structuralKey,
            Array.AsReadOnly(Array.Empty<RenderInputReadback>()),
            RenderDescriptionValidation.CopyResources(resources, nameof(resources)),
            backendBoundary,
            directReplay: null);
    }

    private static void ThrowIfUndefined(RenderDeviceGridSensitivity deviceGridSensitivity)
    {
        if (!Enum.IsDefined(deviceGridSensitivity))
            throw new ArgumentOutOfRangeException(nameof(deviceGridSensitivity));
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
    private readonly IReadOnlyList<RenderResource> _resources;
    private readonly IReadOnlyList<RenderExecutionInput> _inputs;

    internal EngineDirectRenderSession(
        RenderExecutionSessionToken token,
        ImmediateCanvas canvas,
        IReadOnlyList<RenderExecutionInput> inputs,
        IReadOnlyList<RenderResource> resources)
    {
        _token = token;
        Canvas = canvas;
        _inputs = inputs;
        _resources = resources;
    }

    internal ImmediateCanvas Canvas { get; }

    internal RenderExecutionSessionToken Token => _token;

    internal IReadOnlyList<RenderExecutionInput> Inputs
    {
        get { _token.ThrowIfInactive(); return _inputs; }
    }

    internal void UseResource<T>(RenderResource<T> resource, Action<T> use)
        where T : class
    {
        _token.UseResource(resource, _resources, use);
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

    private OpaqueRenderBoundsContract(Rect sourceBounds)
    {
        Kind = OpaqueRenderBoundsKind.Source;
        _sourceBounds = sourceBounds;
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
        object? structuralKey)
    {
        Kind = kind;
        _transformBounds = transformBounds;
        _getRequiredInputBounds = getRequiredInputBounds;
        StructuralIdentity = structuralKey is null
            ? new OpaqueRenderBoundsStructuralIdentity(
                kind,
                transformBounds.Method,
                getRequiredInputBounds?.Method,
                null)
            : new OpaqueRenderBoundsStructuralIdentity(kind, null, null, structuralKey);
    }

    public static OpaqueRenderBoundsContract Source(Rect outputBounds)
    {
        RenderRectValidation.ThrowIfInvalidInput(outputBounds, nameof(outputBounds));
        return new OpaqueRenderBoundsContract(outputBounds);
    }

    public static OpaqueRenderBoundsContract Map(RenderBoundsContract bounds)
    {
        bounds.ThrowIfUninitialized(nameof(bounds));
        return new OpaqueRenderBoundsContract(bounds);
    }

    public static OpaqueRenderBoundsContract Combine(
        Func<IReadOnlyList<Rect>, Rect> transformBounds,
        Func<Rect, IReadOnlyList<Rect>, IReadOnlyList<Rect>> getRequiredInputBounds,
        object? structuralKey = null)
    {
        ArgumentNullException.ThrowIfNull(transformBounds);
        ArgumentNullException.ThrowIfNull(getRequiredInputBounds);
        RenderDescriptionValidation.ValidatePureMetadataCallback(transformBounds, nameof(transformBounds));
        RenderDescriptionValidation.ValidatePureMetadataCallback(
            getRequiredInputBounds,
            nameof(getRequiredInputBounds));
        if (structuralKey is not null)
        {
            RenderIdentityKeyValidator.ThrowIfInvalid(structuralKey, nameof(structuralKey));
        }

        return new OpaqueRenderBoundsContract(
            OpaqueRenderBoundsKind.Combine,
            transformBounds,
            getRequiredInputBounds,
            structuralKey);
    }

    public static OpaqueRenderBoundsContract FullInputs(
        Func<IReadOnlyList<Rect>, Rect> transformBounds,
        object? structuralKey = null)
    {
        ArgumentNullException.ThrowIfNull(transformBounds);
        RenderDescriptionValidation.ValidatePureMetadataCallback(transformBounds, nameof(transformBounds));
        if (structuralKey is not null)
        {
            RenderIdentityKeyValidator.ThrowIfInvalid(structuralKey, nameof(structuralKey));
        }

        return new OpaqueRenderBoundsContract(
            OpaqueRenderBoundsKind.FullInputs,
            transformBounds,
            null,
            structuralKey);
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
        Func<RenderHitTestContext, Point, bool> hitTest,
        object? structuralKey = null)
    {
        ArgumentNullException.ThrowIfNull(hitTest);
        RenderDescriptionValidation.ValidatePureMetadataCallback(hitTest, nameof(hitTest));
        object identity = RenderDescriptionValidation.ResolveStructuralKey(
            structuralKey,
            hitTest.Method,
            nameof(structuralKey));
        return new RenderHitTestContract(hitTest, identity);
    }

    internal static RenderHitTestContract FromResource<T>(
        RenderResource<T> resource,
        Func<T, Point, bool> hitTest,
        object structuralKey)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(hitTest);
        RenderIdentityKeyValidator.ThrowIfInvalid(structuralKey, nameof(structuralKey));
        return new RenderHitTestContract(
            (_, point) => resource.Registry.Use(resource, value => hitTest(value, point)),
            structuralKey);
    }

    internal static RenderHitTestContract FromResource<T>(
        RenderResource<T> resource,
        Func<T, RenderHitTestContext, Point, bool> hitTest,
        object structuralKey)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(hitTest);
        RenderIdentityKeyValidator.ThrowIfInvalid(structuralKey, nameof(structuralKey));
        return new RenderHitTestContract(
            (context, point) => resource.Registry.Use(
                resource,
                value => hitTest(value, context, point)),
            structuralKey);
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
        Point point)
    {
        ThrowIfNotInitialized();
        RenderRectValidation.ThrowIfInvalidInput(outputBounds, nameof(outputBounds));
        ArgumentNullException.ThrowIfNull(inputs);

        return _kind switch
        {
            RenderHitTestContractKind.None => false,
            RenderHitTestContractKind.OutputBounds => outputBounds.Contains(point),
            RenderHitTestContractKind.AnyInput => inputs.Any(input => input.HitTest(point)),
            RenderHitTestContractKind.Custom => _hitTest!(new RenderHitTestContext(outputBounds, inputs), point),
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
    internal RenderHitTestContext(Rect outputBounds, IReadOnlyList<RenderHitTestInput> inputs)
    {
        OutputBounds = outputBounds;
        Inputs = inputs is ReadOnlyCollection<RenderHitTestInput>
            ? inputs
            : Array.AsReadOnly(inputs.ToArray());
    }

    public Rect OutputBounds { get; }

    public IReadOnlyList<RenderHitTestInput> Inputs { get; }
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
    private readonly object? _structuralIdentity;

    private RenderScaleContract(RenderScaleContractKind kind)
    {
        _kind = kind;
        _resolve = null;
        _mapInputSupply = null;
        _structuralIdentity = kind;
    }

    private RenderScaleContract(Func<RenderScaleContext, float> resolve, object structuralIdentity)
    {
        _kind = RenderScaleContractKind.Custom;
        _resolve = resolve;
        _mapInputSupply = null;
        _structuralIdentity = structuralIdentity;
    }

    private RenderScaleContract(
        Func<EffectiveScale, EffectiveScale> mapInputSupply,
        object structuralIdentity)
    {
        _kind = RenderScaleContractKind.MapInputSupply;
        _resolve = null;
        _mapInputSupply = mapInputSupply;
        _structuralIdentity = new RenderScaleContractStructuralIdentity(_kind, structuralIdentity);
    }

    public static RenderScaleContract Vector { get; } = new(RenderScaleContractKind.Vector);

    public static RenderScaleContract PreserveInputSupply { get; } = new(RenderScaleContractKind.PreserveInputSupply);

    public static RenderScaleContract MaterializeAtWorkingScale { get; } =
        new(RenderScaleContractKind.MaterializeAtWorkingScale);

    /// <summary>
    /// Maps the resolved supply metadata of an element-wise one-input operation.
    /// </summary>
    /// <param name="map">
    /// A pure metadata callback that maps the corresponding input supply to the output supply.
    /// The callback may return <see cref="EffectiveScale.Unbounded"/>.
    /// </param>
    /// <param name="structuralKey">
    /// An optional immutable key that identifies the mapping shape independently of runtime values.
    /// </param>
    /// <returns>A declarative one-input supply mapping contract.</returns>
    /// <remarks>
    /// The callback may be evaluated again during graph-wide metadata resolution when an upstream fragment has
    /// symbolic recording metadata, so it must remain deterministic and side-effect-free.
    /// </remarks>
    public static RenderScaleContract MapInputSupply(
        Func<EffectiveScale, EffectiveScale> map,
        object? structuralKey = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        RenderDescriptionValidation.ValidatePureMetadataCallback(map, nameof(map));
        object identity = RenderDescriptionValidation.ResolveStructuralKey(
            structuralKey,
            map.Method,
            nameof(structuralKey));
        return new RenderScaleContract(map, identity);
    }

    public static RenderScaleContract Custom(
        Func<RenderScaleContext, float> resolve,
        object? structuralKey = null)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        RenderDescriptionValidation.ValidatePureMetadataCallback(resolve, nameof(resolve));
        object identity = RenderDescriptionValidation.ResolveStructuralKey(
            structuralKey,
            resolve.Method,
            nameof(structuralKey));
        return new RenderScaleContract(resolve, identity);
    }

    internal RenderScaleContractKind Kind => _kind;

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
                    "MapInputSupply requires exactly one corresponding input supply.");
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
                $"{_kind} is valid only for an element-wise one-input opaque map.",
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
        IReadOnlyList<RenderResource> resources,
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
        _resources = resources;
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

    public void UseResource<T>(RenderResource<T> resource, Action<T> use)
        where T : class
    {
        _token.UseResource(resource, _resources, use);
    }

    /// <summary>Uses a resource by its position in the description's declared resource list.</summary>
    /// <remarks>
    /// The addressing mode a non-capturing callback needs: a resource token is request-scoped and can never be
    /// part of a persistent identity, so it cannot travel through the description's state. The position is the
    /// only address, and <typeparamref name="T"/> is the only check on it: two declared resources of the same
    /// type make index 0 and index 1 indistinguishable, so prepending or reordering <c>resources</c> silently
    /// swaps which one this call reaches.
    /// </remarks>
    public void UseDeclaredResource<T>(int declaredIndex, Action<T> use)
        where T : class
    {
        _token.UseDeclaredResource(declaredIndex, _resources, use);
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

internal readonly record struct OpaqueRenderStructuralIdentity(
    OpaqueRenderTopology Topology,
    object DescriptionKey,
    RenderDeviceGridSensitivity DeviceGridSensitivity,
    RenderBackendBoundary BackendBoundary,
    bool HasEngineDirectReplay);

internal static class RenderDescriptionValidation
{
    /// <summary>
    /// Binds a non-capturing callback to the state that becomes its complete runtime identity.
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
    /// Enforces the state-passing rule: the callback carries no captured value and the state is a valid
    /// output-cache runtime identity.
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

        if (RenderIdentityKeyValidator.CapturesState(execute))
        {
            throw new ArgumentException(
                $"A state-passing execution callback must not capture: '{stateParameterName}' is the only "
                + "channel a per-frame value may reach it through, and is the output-cache runtime identity. "
                + $"Move the captured value into '{stateParameterName}' and declare the callback static, or "
                + "record through CreateRequestLocal when the value cannot be a lightweight immutable key.",
                executeParameterName);
        }

        RenderIdentityKeyValidator.ThrowIfInvalidState(state, stateParameterName);
    }

    public static RenderExecutionChannel<TSession> CreateRequestLocalChannel<TSession>(
        Action<TSession> execute,
        string executeParameterName)
    {
        ArgumentNullException.ThrowIfNull(execute, executeParameterName);
        return RenderExecutionChannel<TSession>.RequestLocal(execute);
    }

    public static RenderExecutionChannel<TSession> CreateDeclaredIdentityChannel<TSession>(
        Action<TSession> execute,
        RenderRuntimeIdentity? runtimeIdentity,
        string executeParameterName,
        string identityParameterName)
    {
        ArgumentNullException.ThrowIfNull(execute, executeParameterName);
        ValidateRuntimeIdentity(runtimeIdentity, identityParameterName);
        return RenderExecutionChannel<TSession>.DeclaredIdentity(execute, runtimeIdentity);
    }

    public static RenderRuntimeIdentity? ResolveRuntimeIdentity<TSession>(
        RenderExecutionChannel<TSession> execution)
        => execution.IdentityKey is { } key ? new RenderRuntimeIdentity(key) : null;

    public static object ResolveStructuralKey(
        object? structuralKey,
        MethodInfo callbackMethod,
        string parameterName)
    {
        if (structuralKey is null)
            return callbackMethod;

        ThrowIfExecutionFacadeIdentity(structuralKey, parameterName);
        RenderIdentityKeyValidator.ThrowIfInvalid(structuralKey, parameterName);
        return structuralKey;
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

    public static void ValidateRuntimeIdentity(RenderRuntimeIdentity? runtimeIdentity, string parameterName)
    {
        if (runtimeIdentity is not { } value)
            return;

        value.ThrowIfUninitialized(parameterName);
        ThrowIfExecutionFacadeIdentity(value.Key, parameterName);
        RenderIdentityKeyValidator.ThrowIfInvalid(value.Key, parameterName);
    }

    public static void ValidatePureMetadataCallback(Delegate callback, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(callback);
        object? target = callback.Target;
        if (target is null)
            return;

        ThrowIfExecutionFacadeIdentity(target, parameterName);
        RenderIdentityKeyValidator.ThrowIfInvalid(target, parameterName);

        foreach (FieldInfo field in target.GetType().GetFields(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            object? captured = field.GetValue(target);
            if (captured is null)
                continue;

            ThrowIfExecutionFacadeIdentity(captured, parameterName);
            try
            {
                RenderIdentityKeyValidator.ThrowIfInvalid(captured, parameterName);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException(
                    "A pure metadata callback cannot capture a mutable value, resource, execution facade, or disposable object.",
                    parameterName,
                    ex);
            }
        }
    }

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
            or PaintedRenderSession
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
