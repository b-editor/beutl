using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering;

/// <summary>Describes concrete recording-time metadata for a render fragment.</summary>
/// <param name="Bounds">The fragment's conservative logical value or query bounds.</param>
/// <param name="EffectiveScale">The density at which the fragment can supply materializable values.</param>
public readonly record struct RenderFragmentMetadata(Rect Bounds, EffectiveScale EffectiveScale);

/// <summary>
/// Identifies a fragment recorded by the active <see cref="RenderNodeContext"/> transaction.
/// </summary>
/// <remarks>
/// A handle is a borrowed, non-executable view of one ordered fragment stream; it is not necessarily
/// one bitmap and does not own resources. Handles are transaction-scoped. Every public member throws
/// <see cref="InvalidOperationException"/> after the owning node's
/// <see cref="RenderNode.Process(RenderNodeContext)"/> call completes.
/// </remarks>
public sealed class RenderFragmentHandle
{
    private readonly IRenderFragmentHandleOwner _owner;
    private readonly RenderFragmentReference _reference;

    internal RenderFragmentHandle(
        IRenderFragmentHandleOwner owner,
        RenderFragmentReference reference)
    {
        _owner = owner;
        _reference = reference;
    }

    /// <summary>Tries to get concrete recording-time bounds and effective-scale metadata.</summary>
    /// <param name="metadata">
    /// Receives the concrete metadata, or <see langword="default"/> when the fragment still depends on an
    /// unresolved owning target domain.
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="metadata"/> is concrete and author-readable.</returns>
    /// <remarks>This method does not execute deferred work or resolve graph-wide regions of interest.</remarks>
    public bool TryGetMetadata(out RenderFragmentMetadata metadata)
    {
        VerifyActive();
        if (!_reference.HasConcreteRecordingMetadata)
        {
            metadata = default;
            return false;
        }

        metadata = new RenderFragmentMetadata(
            _reference.RecordedBounds,
            _reference.RecordedEffectiveScale);
        return true;
    }

    /// <summary>Gets the declared number of materializable values the fragment may produce.</summary>
    public RenderValueCardinality ValueCardinality
    {
        get
        {
            VerifyActive();
            return _reference.ValueCardinality;
        }
    }

    /// <summary>Gets whether publishing the fragment automatically composites its values into the target.</summary>
    /// <remarks>
    /// A value may be non-contributing, and a target-effect fragment may still mutate or read the target
    /// when this property is <see langword="false"/>.
    /// </remarks>
    public bool ContributesValuesToTarget
    {
        get
        {
            VerifyActive();
            return _reference.ContributesValuesToTarget;
        }
    }

    /// <summary>Gets whether the complete fragment stream may be consumed by another value-producing fragment.</summary>
    /// <remarks>
    /// This is conservative recording metadata, not a promise that the fragment is pure or independent of
    /// target-token dependencies.
    /// </remarks>
    public bool CanBeUsedAsValueInput
    {
        get
        {
            VerifyActive();
            return _reference.CanBeUsedAsValueInput;
        }
    }

    /// <summary>Tries to evaluate the fragment's concrete recorded CPU-only hit-test contract.</summary>
    /// <param name="point">The point in the fragment's request coordinate space.</param>
    /// <param name="result">
    /// Receives the hit-test result, or <see langword="false"/> when the fragment still depends on an unresolved
    /// owning target domain.
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="result"/> was evaluated from concrete metadata.</returns>
    /// <remarks>This method does not execute deferred rendering or pixel readback.</remarks>
    public bool TryHitTest(Point point, out bool result)
    {
        VerifyActive();
        if (!_reference.HasConcreteRecordingMetadata)
        {
            result = false;
            return false;
        }

        result = _reference.HitTest(point);
        return true;
    }

    internal RenderFragmentReference GetReference(IRenderFragmentHandleOwner owner)
    {
        VerifyActive();
        if (!ReferenceEquals(_owner, owner))
        {
            throw new InvalidOperationException(
                "The render fragment handle belongs to a different recording transaction.");
        }

        return _reference;
    }

    private void VerifyActive()
    {
        _owner.VerifyActive();
        _owner.VerifyOwns(_reference);
    }
}

internal interface IRenderFragmentHandleOwner
{
    void VerifyActive();

    void VerifyOwns(RenderFragmentReference reference);
}

internal sealed class RenderFragmentReference
{
    private Func<Point, bool> _hitTest;

    public RenderFragmentReference(
        RenderFragmentKind kind,
        Rect bounds,
        EffectiveScale effectiveScale,
        RenderValueCardinality valueCardinality,
        bool contributesValuesToTarget,
        bool canBeUsedAsValueInput,
        bool hasTargetEffects,
        bool hasOpaqueExternalWork,
        IEnumerable<RenderFragmentReference>? inputs,
        object? payload,
        Func<Point, bool>? hitTest,
        RenderFragmentBoundsRequirement boundsRequirement = RenderFragmentBoundsRequirement.Finite)
    {
        valueCardinality.ThrowIfUninitialized(nameof(valueCardinality));
        if (!Enum.IsDefined(boundsRequirement))
            throw new ArgumentOutOfRangeException(nameof(boundsRequirement));
        if (!RenderRectValidation.IsFiniteNonNegative(bounds))
        {
            throw new ArgumentException(
                "Recorded fragment bounds must be finite and have non-negative dimensions.",
                nameof(bounds));
        }

        Kind = kind;
        RecordedBounds = bounds;
        Bounds = bounds;
        RecordedEffectiveScale = effectiveScale;
        EffectiveScale = effectiveScale;
        BoundsRequirement = boundsRequirement;
        ValueCardinality = valueCardinality;
        ContributesValuesToTarget = contributesValuesToTarget;
        CanBeUsedAsValueInput = canBeUsedAsValueInput;
        HasTargetEffects = hasTargetEffects;
        HasOpaqueExternalWork = hasOpaqueExternalWork;
        Inputs = inputs is null ? [] : [.. inputs];
        HasConcreteRecordingMetadata = boundsRequirement == RenderFragmentBoundsRequirement.Finite
            && (kind == RenderFragmentKind.Layer
                || Inputs.All(static input => input.HasConcreteRecordingMetadata));
        HasSymbolicBoundsDependency = boundsRequirement == RenderFragmentBoundsRequirement.OwningTargetDomain
            || Inputs.Any(static input => input.HasSymbolicBoundsDependency);
        Payload = payload;
        PotentiallyWritesTarget = ComputePotentiallyWritesTarget();
        _hitTest = hitTest ?? (static _ => false);
    }

    public RenderFragmentKind Kind { get; }

    public Rect RecordedBounds { get; }

    public Rect Bounds { get; private set; }

    public EffectiveScale RecordedEffectiveScale { get; }

    public EffectiveScale EffectiveScale { get; private set; }

    public RenderFragmentBoundsRequirement BoundsRequirement { get; }

    public bool HasConcreteRecordingMetadata { get; }

    public bool HasSymbolicBoundsDependency { get; }

    public RenderValueCardinality ValueCardinality { get; }

    public bool ContributesValuesToTarget { get; }

    public bool CanBeUsedAsValueInput { get; }

    public bool HasTargetEffects { get; }

    public bool PotentiallyWritesTarget { get; }

    public bool HasOpaqueExternalWork { get; }

    public ImmutableArray<RenderFragmentReference> Inputs { get; }

    public bool SuppressesInputExecution
        => Kind == RenderFragmentKind.TargetLayerScope
           && Payload is TargetLayerScopeRenderFragmentPayload layer
           && layer.Region.Kind == TargetRegionKind.Empty;

    public ImmutableArray<RenderFragmentReference> ExecutionInputs
        => SuppressesInputExecution
            ? ImmutableArray<RenderFragmentReference>.Empty
            : Inputs;

    public object? Payload { get; }

    public RenderFragmentId? Id { get; set; }

    public ImmutableArray<RenderValueId> ValueIds { get; set; } = [];

    public bool AllowsFanOut => CanBeUsedAsValueInput;

    public bool HitTest(Point point)
        => Kind == RenderFragmentKind.LegacyFilterEffect
           && BoundsRequirement == RenderFragmentBoundsRequirement.OwningTargetDomain
            ? Bounds.Contains(point)
            : _hitTest(point);

    public void ApplyResolvedMetadata(
        Rect bounds,
        EffectiveScale effectiveScale,
        Func<Point, bool>? hitTest = null)
    {
        if (!RenderRectValidation.IsFiniteNonNegative(bounds))
        {
            throw new InvalidOperationException(
                "Resolved fragment bounds must be finite and have non-negative dimensions.");
        }

        Bounds = bounds;
        EffectiveScale = effectiveScale;
        if (hitTest is not null)
            _hitTest = hitTest;
    }

    private bool ComputePotentiallyWritesTarget()
    {
        bool replayWrites = Kind == RenderFragmentKind.OpacityMask
            ? Inputs.Length > 0
              && (Inputs[0].ContributesValuesToTarget || Inputs[0].PotentiallyWritesTarget)
            : Inputs.Any(static input =>
                input.ContributesValuesToTarget || input.PotentiallyWritesTarget);
        return Kind switch
        {
            RenderFragmentKind.TargetCommand
                => Payload is TargetCommandRenderFragmentPayload command
                   && command.Description.Access != TargetAccess.Readback
                   && command.Description.AffectedRegion.Kind != TargetRegionKind.Empty,
            RenderFragmentKind.RawTargetCommand => true,
            RenderFragmentKind.TargetCapture or RenderFragmentKind.BuiltInBackdropCapture => false,
            RenderFragmentKind.TargetLayerScope
                => Payload is TargetLayerScopeRenderFragmentPayload layer
                   && layer.Region.Kind != TargetRegionKind.Empty
                   && replayWrites,
            RenderFragmentKind.TargetScope
                or RenderFragmentKind.Blend
                or RenderFragmentKind.Opacity
                or RenderFragmentKind.OpacityMask
                => replayWrites,
            RenderFragmentKind.RawTargetScope => true,
            _ => false,
        };
    }
}

internal enum RenderFragmentBoundsRequirement : byte
{
    Finite,
    OwningTargetDomain,
}

internal static class TargetWriteMetadataResolver
{
    public static bool TryResolveFinite(
        RenderFragmentReference reference,
        out Rect? affectedBounds)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!reference.PotentiallyWritesTarget)
        {
            affectedBounds = null;
            return true;
        }

        switch (reference.Kind)
        {
            case RenderFragmentKind.TargetCommand:
                return TryResolveRegion(
                    ((TargetCommandRenderFragmentPayload)reference.Payload!).Description.AffectedRegion,
                    targetDomain: null,
                    out affectedBounds);
            case RenderFragmentKind.RawTargetCommand:
            case RenderFragmentKind.RawTargetScope:
                affectedBounds = null;
                return false;
            case RenderFragmentKind.TargetLayerScope:
                return TryResolveRegion(
                    ((TargetLayerScopeRenderFragmentPayload)reference.Payload!).Region,
                    targetDomain: null,
                    out affectedBounds);
            case RenderFragmentKind.TargetScope:
                return TryResolveFiniteTargetScope(reference, out affectedBounds);
            case RenderFragmentKind.Blend:
            case RenderFragmentKind.Opacity:
            case RenderFragmentKind.OpacityMask:
                return TryResolveFiniteReplay(reference, out affectedBounds);
            default:
                affectedBounds = null;
                return false;
        }
    }

    public static Rect? Resolve(
        RenderFragmentReference reference,
        Rect? targetDomain)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!reference.PotentiallyWritesTarget)
            return null;

        return reference.Kind switch
        {
            RenderFragmentKind.TargetCommand
                => ResolveRegion(
                    ((TargetCommandRenderFragmentPayload)reference.Payload!).Description.AffectedRegion,
                    targetDomain),
            RenderFragmentKind.RawTargetCommand or RenderFragmentKind.RawTargetScope
                => ResolveRegion(TargetRegion.Full, targetDomain),
            RenderFragmentKind.TargetLayerScope
                => ResolveRegion(
                    ((TargetLayerScopeRenderFragmentPayload)reference.Payload!).Region,
                    targetDomain),
            RenderFragmentKind.TargetScope
                => ResolveTargetScope(reference, targetDomain),
            RenderFragmentKind.Blend
                or RenderFragmentKind.Opacity
                or RenderFragmentKind.OpacityMask
                => ResolveReplayBounds(reference, targetDomain),
            _ => null,
        };
    }

    private static bool TryResolveFiniteTargetScope(
        RenderFragmentReference reference,
        out Rect? affectedBounds)
    {
        if (!TryResolveFiniteReplay(reference, out Rect? replayBounds))
        {
            affectedBounds = null;
            return false;
        }

        if (replayBounds is not { } bounds)
        {
            affectedBounds = null;
            return true;
        }

        affectedBounds = ((TargetScopeRenderFragmentPayload)reference.Payload!)
            .Description.Bounds.TransformBounds(bounds);
        return true;
    }

    private static bool TryResolveFiniteReplay(
        RenderFragmentReference reference,
        out Rect? affectedBounds)
    {
        Rect result = default;
        bool hasBounds = false;
        int inputCount = reference.Kind == RenderFragmentKind.OpacityMask
            ? Math.Min(1, reference.Inputs.Length)
            : reference.Inputs.Length;
        for (int i = 0; i < inputCount; i++)
        {
            RenderFragmentReference input = reference.Inputs[i];
            if (input.ContributesValuesToTarget)
            {
                if (!input.HasConcreteRecordingMetadata)
                {
                    affectedBounds = null;
                    return false;
                }

                result = result.Union(input.RecordedBounds);
                hasBounds = true;
            }

            if (!TryResolveFinite(input, out Rect? inputAffectedBounds))
            {
                affectedBounds = null;
                return false;
            }

            if (inputAffectedBounds is { } affected)
            {
                result = result.Union(affected);
                hasBounds = true;
            }
        }

        affectedBounds = hasBounds ? result : null;
        return true;
    }

    private static bool TryResolveRegion(
        TargetRegion region,
        Rect? targetDomain,
        out Rect? affectedBounds)
    {
        switch (region.Kind)
        {
            case TargetRegionKind.Empty:
                affectedBounds = null;
                return true;
            case TargetRegionKind.Region:
                affectedBounds = region.Value;
                return true;
            case TargetRegionKind.Full when targetDomain is { } domain:
                affectedBounds = domain;
                return true;
            case TargetRegionKind.Full:
                affectedBounds = null;
                return false;
            default:
                throw new InvalidOperationException("The target region is uninitialized.");
        }
    }

    private static Rect? ResolveTargetScope(
        RenderFragmentReference reference,
        Rect? targetDomain)
    {
        var payload = (TargetScopeRenderFragmentPayload)reference.Payload!;
        Rect? localDomain = targetDomain is { } domain
            ? payload.Description.Bounds.GetRequiredInputBounds(domain)
            : null;
        Rect? replayBounds = ResolveReplayBounds(reference, localDomain);
        if (replayBounds is not { } bounds)
            return null;

        return payload.Description.Bounds.TransformBounds(bounds);
    }

    private static Rect? ResolveReplayBounds(
        RenderFragmentReference reference,
        Rect? targetDomain)
    {
        Rect result = default;
        bool hasBounds = false;
        int inputCount = reference.Kind == RenderFragmentKind.OpacityMask
            ? Math.Min(1, reference.Inputs.Length)
            : reference.Inputs.Length;
        for (int i = 0; i < inputCount; i++)
        {
            RenderFragmentReference input = reference.Inputs[i];
            if (input.ContributesValuesToTarget)
            {
                result = result.Union(input.Bounds);
                hasBounds = true;
            }

            if (Resolve(input, targetDomain) is { } affected)
            {
                result = result.Union(affected);
                hasBounds = true;
            }
        }

        return hasBounds ? result : null;
    }

    private static Rect? ResolveRegion(TargetRegion region, Rect? targetDomain)
    {
        return region.Kind switch
        {
            TargetRegionKind.Empty => null,
            TargetRegionKind.Region => region.Value,
            TargetRegionKind.Full when targetDomain is { } domain => domain,
            TargetRegionKind.Full => throw new InvalidOperationException(
                "A target-less request with a Full target write requires a finite TargetDomain."),
            _ => throw new InvalidOperationException("The target region is uninitialized."),
        };
    }
}

internal static class RenderFragmentTargetDependency
{
    public static bool HasExternalTargetDependency(RenderFragmentReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var visited = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        return Visit(reference, visited);
    }

    private static bool Visit(
        RenderFragmentReference reference,
        ISet<RenderFragmentReference> visited)
    {
        if (!visited.Add(reference))
            return false;

        if (reference.Kind == RenderFragmentKind.Layer)
        {
            // A finite Layer owns a fresh transparent target. Target operations below it are
            // self-contained inputs to the resulting value, not dependencies on the caller's target token.
            return false;
        }

        if (reference.Kind is RenderFragmentKind.TargetCapture
            or RenderFragmentKind.BuiltInBackdropCapture
            or RenderFragmentKind.TargetCommand
            or RenderFragmentKind.RawTargetCommand
            or RenderFragmentKind.TargetLayerScope
            or RenderFragmentKind.RawTargetScope)
        {
            return true;
        }

        if (reference.Kind == RenderFragmentKind.TargetScope
            && ((TargetScopeRenderFragmentPayload)reference.Payload!).Description.IsValueReplayMap is false)
        {
            return true;
        }

        return reference.Inputs.Any(input => Visit(input, visited));
    }
}

internal enum RenderFragmentKind : byte
{
    ContributeValues,
    Opacity,
    Blend,
    OpacityMask,
    Shader,
    Geometry,
    OpaqueSource,
    OpaqueMap,
    OpaqueCombine,
    OpaqueExpand,
    LegacyFilterEffect,
    MaterializedInput,
    TargetCapture,
    Layer,
    TargetLayerScope,
    TargetScope,
    RawTargetScope,
    RawTargetCommand,
    TargetCommand,
    BuiltInBackdropCapture,
}
