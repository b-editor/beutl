using System.Collections.Immutable;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.Graphics.Rendering;

internal sealed class RenderFragmentReference
{
    // Holds the recorded rule until planning lowers it.
    private RenderFragmentHitTest _hitTest;

    /// <remarks><paramref name="inputs"/> is stored as given, not copied.</remarks>
    public RenderFragmentReference(
        RenderFragmentKind kind,
        Rect bounds,
        EffectiveScale effectiveScale,
        RenderValueCardinality valueCardinality,
        bool contributesValuesToTarget,
        bool canBeUsedAsValueInput,
        bool hasTargetEffects,
        bool hasOpaqueExternalWork,
        ImmutableArray<RenderFragmentReference> inputs,
        object? payload,
        RenderFragmentHitTest hitTest,
        RenderFragmentBoundsRequirement boundsRequirement = RenderFragmentBoundsRequirement.Finite,
        bool hasDirectSymbolicBoundsDependency = false)
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
        Inputs = inputs.IsDefault ? [] : inputs;
        SupportsIndependentOutputDensities = payload is OpaqueRenderFragmentPayload
            || (kind == RenderFragmentKind.ContributeValues
                && Inputs.Length == 1
                && Inputs[0].SupportsIndependentOutputDensities);
        HasConcreteRecordingMetadata = !hasDirectSymbolicBoundsDependency
            && boundsRequirement == RenderFragmentBoundsRequirement.Finite
            && (kind == RenderFragmentKind.Layer
                || Inputs.All(static input => input.HasConcreteRecordingMetadata));
        HasSymbolicBoundsDependency = hasDirectSymbolicBoundsDependency
            || boundsRequirement == RenderFragmentBoundsRequirement.OwningTargetDomain
            || Inputs.Any(static input => input.HasSymbolicBoundsDependency);
        Payload = payload;
        PotentiallyWritesTarget = ComputePotentiallyWritesTarget();
        HasSymbolicTargetWrite = ComputeHasSymbolicTargetWrite();
        ClearsTarget = ComputeClearsTarget();
        _hitTest = hitTest;
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

    /// <summary>
    /// Gets whether this fragment writes target pixels that <see cref="RecordedBounds"/> does not describe.
    /// </summary>
    /// <remarks>
    /// A full-target write - a clear, an opaque raw command - states its extent symbolically and contributes
    /// no value bounds, so a consumer that scopes by recorded bounds alone would clip it away entirely.
    /// A finite region restores a described extent: it bounds what the scope can reach whatever is inside it.
    /// </remarks>
    public bool HasSymbolicTargetWrite { get; }

    // Ordinary off-screen layers contain clears; sampling layers defer them until the combined output.
    internal bool ClearsTarget { get; }

    public bool HasOpaqueExternalWork { get; }

    public bool SupportsIndependentOutputDensities { get; }

    public ImmutableArray<RenderFragmentReference> Inputs { get; }

    public bool SuppressesInputExecution
        => Kind == RenderFragmentKind.TargetLayerScope
           && Payload is TargetLayerScopeRenderFragmentPayload layer
           && layer.Region.Kind == TargetRegionKind.Empty;

    public ImmutableArray<RenderFragmentReference> ExecutionInputs
        => SuppressesInputExecution
            ? ImmutableArray<RenderFragmentReference>.Empty
            : Inputs;

    public object? Payload { get; private set; }

    public RenderFragmentId? Id { get; private set; }

    public bool AllowsFanOut => CanBeUsedAsValueInput;

    internal void AssignId(RenderFragmentId id)
    {
        if (id.RequestId.Value <= 0 || id.Value <= 0)
            throw new ArgumentException("A fragment requires an initialized graph ID.", nameof(id));
        if (Id is not null)
            throw new InvalidOperationException("A recorded fragment was already committed to a graph.");

        Id = id;
    }

    public bool HitTest(Point point) => _hitTest.Evaluate(Bounds, Inputs, point);

    // Target scopes and raw target scopes carry the bounds contract that maps their content onto the enclosing
    // target; no other payload does.
    internal bool TryGetScopeBoundsContract(out RenderBoundsContract bounds)
    {
        switch (Payload)
        {
            case TargetScopeRenderFragmentPayload scope:
                bounds = scope.Description.Bounds;
                return true;
            case RawTargetScopeRenderFragmentPayload scope:
                bounds = scope.Description.Bounds;
                return true;
            default:
                bounds = default;
                return false;
        }
    }

    /// <summary>Replaces the payload this fragment was recorded with by the one resolution derived for it.</summary>
    /// <remarks>
    /// A fragment reference belongs to one request, so the next request resolves the declaration again over its
    /// own ambient.
    /// </remarks>
    internal void ApplyResolvedPayload(object payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Payload = payload;
    }

    public void ApplyResolvedMetadata(
        Rect bounds,
        EffectiveScale effectiveScale,
        RenderFragmentHitTest? hitTest = null)
    {
        if (!RenderRectValidation.IsFiniteNonNegative(bounds))
        {
            throw new InvalidOperationException(
                "Resolved fragment bounds must be finite and have non-negative dimensions.");
        }

        Bounds = bounds;
        EffectiveScale = effectiveScale;
        if (hitTest is { } resolved)
            _hitTest = resolved;
    }

    private bool ComputeHasSymbolicTargetWrite()
    {
        bool inputsWriteSymbolically = Inputs.Any(static input => input.HasSymbolicTargetWrite);
        return Kind switch
        {
            RenderFragmentKind.TargetCommand
                => Payload is TargetCommandRenderFragmentPayload command
                   && command.Description.AffectedRegion.Kind == TargetRegionKind.Full,
            RenderFragmentKind.RawTargetCommand or RenderFragmentKind.RawTargetScope => true,
            RenderFragmentKind.TargetCapture or RenderFragmentKind.BuiltInBackdropCapture => false,
            RenderFragmentKind.TargetLayerScope
                => Payload is TargetLayerScopeRenderFragmentPayload layer
                   && layer.Region.Kind == TargetRegionKind.Full
                   && inputsWriteSymbolically,
            RenderFragmentKind.ContributeValues
                or RenderFragmentKind.Opacity
                or RenderFragmentKind.Blend
                or RenderFragmentKind.OpacityMask
                or RenderFragmentKind.Shader
                or RenderFragmentKind.ShaderGraph
                or RenderFragmentKind.Geometry
                or RenderFragmentKind.OpaqueSource
                or RenderFragmentKind.OpaqueMap
                or RenderFragmentKind.OpaqueCombine
                or RenderFragmentKind.OpaqueExpand
                or RenderFragmentKind.FilterEffectSegment
                or RenderFragmentKind.MaterializedInput
                or RenderFragmentKind.Layer
                or RenderFragmentKind.TargetScope
                => inputsWriteSymbolically,
            _ => throw new InvalidOperationException(
                $"Fragment kind '{Kind}' has no symbolic target-write rule."),
        };
    }

    private bool ComputeClearsTarget() => Kind switch
    {
        RenderFragmentKind.TargetCommand => Payload is TargetCommandRenderFragmentPayload { ClearsTarget: true },
        RenderFragmentKind.Blend when Payload is BlendRenderFragmentPayload { BlendMode: BlendMode.Src } => true,
        RenderFragmentKind.TargetLayerScope => Payload is TargetLayerScopeRenderFragmentPayload
        { PreservesTargetClear: true, Region.Kind: not TargetRegionKind.Empty }
            && Inputs.Any(static input => input.ClearsTarget),
        RenderFragmentKind.Layer or RenderFragmentKind.TargetCapture or RenderFragmentKind.BuiltInBackdropCapture => false,
        _ => Inputs.Any(static input => input.ClearsTarget),
    };

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
            RenderFragmentKind.ContributeValues
                or RenderFragmentKind.Shader
                or RenderFragmentKind.ShaderGraph
                or RenderFragmentKind.Geometry
                or RenderFragmentKind.OpaqueSource
                or RenderFragmentKind.OpaqueMap
                or RenderFragmentKind.OpaqueCombine
                or RenderFragmentKind.OpaqueExpand
                or RenderFragmentKind.FilterEffectSegment
                or RenderFragmentKind.MaterializedInput
                or RenderFragmentKind.Layer
                => false,
            _ => throw new InvalidOperationException(
                $"Fragment kind '{Kind}' has no target-write rule."),
        };
    }
}
