using System.Collections.ObjectModel;
using System.Reflection;
using Beutl.Graphics.Effects;
using Beutl.Media;

namespace Beutl.Graphics.Rendering;

public sealed class OpaqueRenderDescription
{
    private OpaqueRenderDescription(
        Action<OpaqueRenderSession> execute,
        RenderOperationBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        object structuralKey,
        RenderRuntimeIdentity? runtimeIdentity,
        bool requiresReadback,
        IReadOnlyList<RenderResource> resources,
        RenderBackendBoundary backendBoundary,
        Action<EngineDirectRenderSession>? directReplay)
    {
        Execute = execute;
        Bounds = bounds;
        HitTest = hitTest;
        ValueCardinality = valueCardinality;
        Scale = scale;
        StructuralKey = structuralKey;
        RuntimeIdentity = runtimeIdentity;
        RequiresReadback = requiresReadback;
        Resources = resources;
        BackendBoundary = backendBoundary;
        DirectReplay = directReplay;
    }

    public RenderOperationBoundsContract Bounds { get; }

    public RenderHitTestContract HitTest { get; }

    public RenderValueCardinality ValueCardinality { get; }

    public RenderScaleContract Scale { get; }

    public bool RequiresReadback { get; }

    public object StructuralKey { get; }

    public RenderRuntimeIdentity? RuntimeIdentity { get; }

    public IReadOnlyList<RenderResource> Resources { get; }

    internal Action<OpaqueRenderSession> Execute { get; }

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
            BackendBoundary,
            DirectReplay is not null);

    internal OpaqueRenderDescription WithoutDirectReplay()
        => DirectReplay is null
            ? this
            : new OpaqueRenderDescription(
                Execute,
                Bounds,
                HitTest,
                ValueCardinality,
                Scale,
                StructuralKey,
                RuntimeIdentity,
                RequiresReadback,
                Resources,
                BackendBoundary,
                directReplay: null);

    public static OpaqueRenderDescription Create(
        Action<OpaqueRenderSession> execute,
        RenderOperationBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
        object? structuralKey = null,
        RenderRuntimeIdentity? runtimeIdentity = null,
        bool requiresReadback = false,
        IEnumerable<RenderResource>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(bounds);
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        valueCardinality.ThrowIfUninitialized(nameof(valueCardinality));
        scale.ThrowIfUninitialized(nameof(scale));

        object resolvedStructuralKey = RenderDescriptionValidation.ResolveStructuralKey(
            structuralKey,
            execute.Method,
            nameof(structuralKey));
        RenderDescriptionValidation.ValidateRuntimeIdentity(runtimeIdentity, nameof(runtimeIdentity));

        return new OpaqueRenderDescription(
            execute,
            bounds,
            hitTest,
            valueCardinality,
            scale,
            resolvedStructuralKey,
            runtimeIdentity,
            requiresReadback,
            RenderDescriptionValidation.CopyResources(resources, nameof(resources)),
            RenderBackendBoundary.None,
            directReplay: null);
    }

    internal static OpaqueRenderDescription CreateEngineSource(
        Action<OpaqueRenderSession> execute,
        Action<EngineDirectRenderSession> directReplay,
        RenderOperationBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderScaleContract scale,
        object structuralKey,
        RenderRuntimeIdentity? runtimeIdentity,
        IEnumerable<RenderResource>? resources = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(directReplay);
        ArgumentNullException.ThrowIfNull(bounds);
        hitTest.ThrowIfUninitialized(nameof(hitTest));
        scale.ThrowIfUninitialized(nameof(scale));
        ArgumentNullException.ThrowIfNull(structuralKey);
        RenderIdentityKeyValidator.ThrowIfInvalid(structuralKey, nameof(structuralKey));
        RenderDescriptionValidation.ValidateRuntimeIdentity(runtimeIdentity, nameof(runtimeIdentity));

        return new OpaqueRenderDescription(
            execute,
            bounds,
            hitTest,
            RenderValueCardinality.Single,
            scale,
            structuralKey,
            runtimeIdentity,
            requiresReadback: false,
            RenderDescriptionValidation.CopyResources(resources, nameof(resources)),
            RenderBackendBoundary.None,
            directReplay);
    }

    internal static OpaqueRenderDescription CreateBackendBoundary(
        RenderBackendBoundary backendBoundary,
        Action<OpaqueRenderSession> execute,
        RenderOperationBoundsContract bounds,
        RenderHitTestContract hitTest,
        RenderValueCardinality valueCardinality,
        RenderScaleContract scale,
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
        ArgumentNullException.ThrowIfNull(structuralKey);
        RenderIdentityKeyValidator.ThrowIfInvalid(structuralKey, nameof(structuralKey));
        RenderDescriptionValidation.ValidateRuntimeIdentity(runtimeIdentity, nameof(runtimeIdentity));

        return new OpaqueRenderDescription(
            execute,
            bounds,
            hitTest,
            valueCardinality,
            scale,
            structuralKey,
            runtimeIdentity,
            requiresReadback: false,
            RenderDescriptionValidation.CopyResources(resources, nameof(resources)),
            backendBoundary,
            directReplay: null);
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

public sealed class RenderOperationBoundsContract
{
    private readonly Rect _sourceBounds;
    private readonly RenderBoundsContract _mapBounds;
    private readonly Func<IReadOnlyList<Rect>, Rect>? _transformBounds;
    private readonly Func<Rect, IReadOnlyList<Rect>, IReadOnlyList<Rect>>? _getRequiredInputBounds;

    private RenderOperationBoundsContract(Rect sourceBounds)
    {
        Kind = RenderOperationBoundsKind.Source;
        _sourceBounds = sourceBounds;
        StructuralIdentity = new RenderOperationBoundsStructuralIdentity(Kind, null, null, null);
    }

    private RenderOperationBoundsContract(RenderBoundsContract mapBounds)
    {
        Kind = RenderOperationBoundsKind.Map;
        _mapBounds = mapBounds;
        StructuralIdentity = new RenderOperationBoundsStructuralIdentity(
            Kind,
            mapBounds.StructuralIdentity,
            null,
            null);
    }

    private RenderOperationBoundsContract(
        RenderOperationBoundsKind kind,
        Func<IReadOnlyList<Rect>, Rect> transformBounds,
        Func<Rect, IReadOnlyList<Rect>, IReadOnlyList<Rect>>? getRequiredInputBounds,
        object? structuralKey)
    {
        Kind = kind;
        _transformBounds = transformBounds;
        _getRequiredInputBounds = getRequiredInputBounds;
        StructuralIdentity = structuralKey is null
            ? new RenderOperationBoundsStructuralIdentity(
                kind,
                transformBounds.Method,
                getRequiredInputBounds?.Method,
                null)
            : new RenderOperationBoundsStructuralIdentity(kind, null, null, structuralKey);
    }

    public static RenderOperationBoundsContract Source(Rect outputBounds)
    {
        RenderRectValidation.ThrowIfInvalidInput(outputBounds, nameof(outputBounds));
        return new RenderOperationBoundsContract(outputBounds);
    }

    public static RenderOperationBoundsContract Map(RenderBoundsContract bounds)
    {
        bounds.ThrowIfUninitialized(nameof(bounds));
        return new RenderOperationBoundsContract(bounds);
    }

    public static RenderOperationBoundsContract Combine(
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

        return new RenderOperationBoundsContract(
            RenderOperationBoundsKind.Combine,
            transformBounds,
            getRequiredInputBounds,
            structuralKey);
    }

    public static RenderOperationBoundsContract FullInputs(
        Func<IReadOnlyList<Rect>, Rect> transformBounds,
        object? structuralKey = null)
    {
        ArgumentNullException.ThrowIfNull(transformBounds);
        RenderDescriptionValidation.ValidatePureMetadataCallback(transformBounds, nameof(transformBounds));
        if (structuralKey is not null)
        {
            RenderIdentityKeyValidator.ThrowIfInvalid(structuralKey, nameof(structuralKey));
        }

        return new RenderOperationBoundsContract(
            RenderOperationBoundsKind.FullInputs,
            transformBounds,
            null,
            structuralKey);
    }

    internal RenderOperationBoundsKind Kind { get; }

    internal object StructuralIdentity { get; }

    internal Rect TransformBounds(IReadOnlyList<Rect> inputBounds)
    {
        ArgumentNullException.ThrowIfNull(inputBounds);
        ValidateRectangles(inputBounds, nameof(inputBounds));

        Rect result = Kind switch
        {
            RenderOperationBoundsKind.Source when inputBounds.Count == 0 => _sourceBounds,
            RenderOperationBoundsKind.Source => throw new InvalidOperationException(
                "A source bounds contract cannot receive input bounds."),
            RenderOperationBoundsKind.Map when inputBounds.Count == 1 => _mapBounds.TransformBounds(inputBounds[0]),
            RenderOperationBoundsKind.Map => throw new InvalidOperationException(
                "A map bounds contract requires exactly one input bound."),
            RenderOperationBoundsKind.Combine or RenderOperationBoundsKind.FullInputs => _transformBounds!(inputBounds),
            _ => throw new InvalidOperationException("The operation bounds contract is invalid."),
        };

        RenderRectValidation.ThrowIfInvalidResult(
            result,
            "The operation forward bounds mapping returned an invalid rectangle.");
        return result;
    }

    internal IReadOnlyList<Rect> GetRequiredInputBounds(
        Rect requestedOutputBounds,
        IReadOnlyList<Rect> inputBounds)
    {
        RenderRectValidation.ThrowIfInvalidInput(requestedOutputBounds, nameof(requestedOutputBounds));
        ArgumentNullException.ThrowIfNull(inputBounds);
        ValidateRectangles(inputBounds, nameof(inputBounds));

        if (Kind == RenderOperationBoundsKind.Source)
        {
            if (inputBounds.Count != 0)
                throw new InvalidOperationException("A source bounds contract cannot receive input bounds.");

            return Array.Empty<Rect>();
        }

        bool emptyRequirement = requestedOutputBounds.Width == 0 || requestedOutputBounds.Height == 0;
        IReadOnlyList<Rect> result;
        if (Kind == RenderOperationBoundsKind.Map)
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
        else if (Kind == RenderOperationBoundsKind.FullInputs)
        {
            result = emptyRequirement
                ? Enumerable.Repeat(Rect.Empty, inputBounds.Count).ToArray()
                : inputBounds.ToArray();
        }
        else
        {
            result = _getRequiredInputBounds!(requestedOutputBounds, inputBounds)
                ?? throw new InvalidOperationException("The operation backward bounds mapping returned null.");
        }

        if (result.Count != inputBounds.Count)
        {
            throw new InvalidOperationException(
                "The operation backward bounds mapping must return exactly one rectangle per input.");
        }

        ValidateResultRectangles(result);
        return result is ReadOnlyCollection<Rect> ? result : Array.AsReadOnly(result.ToArray());
    }

    internal void ThrowIfIncompatible(OpaqueRenderTopology topology, string parameterName)
    {
        bool compatible = topology switch
        {
            OpaqueRenderTopology.Source => Kind == RenderOperationBoundsKind.Source,
            OpaqueRenderTopology.Map => Kind == RenderOperationBoundsKind.Map,
            OpaqueRenderTopology.Combine or OpaqueRenderTopology.Expand =>
                Kind is RenderOperationBoundsKind.Combine or RenderOperationBoundsKind.FullInputs,
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
                    $"The operation backward bounds mapping returned an invalid rectangle at index {index}.");
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

        resolved = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(outputBounds, resolved);
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
    private readonly Func<OpaqueRenderSession, Rect, OpaqueRenderOutput> _createOutput;
    private readonly Action<OpaqueRenderOutput> _publish;
    private readonly IReadOnlyList<RenderExecutionInput> _inputs;
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
        Rect outputBounds,
        Rect requiredRegion,
        PixelRect deviceBounds,
        float outputScale,
        float workingScale,
        float maxWorkingScale,
        RenderIntent intent,
        RenderRequestPurpose purpose,
        IReadOnlyList<RenderResource> resources,
        Func<OpaqueRenderSession, Rect, OpaqueRenderOutput> createOutput,
        Action<OpaqueRenderOutput> publish)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(createOutput);
        ArgumentNullException.ThrowIfNull(publish);
        _token = token;
        _inputs = Array.AsReadOnly(inputs.ToArray());
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

    public IReadOnlyList<RenderExecutionInput> Inputs
    {
        get { _token.ThrowIfInactive(); return _inputs; }
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

    public OpaqueRenderOutput CreateOutput(Rect logicalBounds)
    {
        _token.ThrowIfInactive();
        RenderDescriptionValidation.ThrowIfFiniteNonEmpty(logicalBounds, nameof(logicalBounds));
        if (!RenderDescriptionValidation.Contains(_outputBounds, logicalBounds))
        {
            throw new ArgumentException("An opaque output must be contained by the declared output bounds.", nameof(logicalBounds));
        }

        return _createOutput(this, logicalBounds);
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

internal enum RenderOperationBoundsKind : byte
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

internal readonly record struct RenderOperationBoundsStructuralIdentity(
    RenderOperationBoundsKind Kind,
    object? ForwardIdentity,
    object? BackwardIdentity,
    object? ExplicitKey);

internal readonly record struct RenderScaleContractStructuralIdentity(
    RenderScaleContractKind Kind,
    object CallbackIdentity);

internal readonly record struct OpaqueRenderStructuralIdentity(
    OpaqueRenderTopology Topology,
    object DescriptionKey,
    RenderBackendBoundary BackendBoundary,
    bool HasEngineDirectReplay);

internal static class RenderDescriptionValidation
{
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
