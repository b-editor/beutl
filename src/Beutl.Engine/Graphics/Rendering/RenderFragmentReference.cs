using System.Collections.Immutable;
using Beutl.Graphics.Rendering.Requests;

namespace Beutl.Graphics.Rendering;

internal sealed class RenderFragmentReference
{
    private readonly bool _hasDirectSymbolicBoundsDependency;

    // Holds the recorded rule until planning lowers it, and a fragment is captured for replay and
    // fingerprinted before planning begins, so both of those read what recording stated. Nothing may clone,
    // digest, or take the recording identity of a fragment after ApplyResolvedMetadata has run on it.
    private RenderFragmentHitTest _hitTest;

    private RenderFragmentRecordingIdentity? _recordingIdentity;

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
        _hasDirectSymbolicBoundsDependency = hasDirectSymbolicBoundsDependency;
        _hitTest = hitTest;
        RecordingFingerprint = ComputeRecordingFingerprint();
    }

    /// <summary>
    /// A digest of everything a consumer can read from this fragment while recording, including its inputs'
    /// own digests.
    /// </summary>
    /// <remarks>
    /// A consumer reaches a fragment through <see cref="RenderFragmentHandle"/>, which exposes recording
    /// metadata and nothing of the payload, so two fragments that agree here are interchangeable to whoever
    /// records above them. It is what lets a reused recording be rebased onto a different request's
    /// fragments: the inputs it is replayed over must digest to what it was recorded over.
    /// </remarks>
    internal long RecordingFingerprint { get; }

    /// <summary>The structure <see cref="RecordingFingerprint"/> digests, taken once and kept.</summary>
    internal RenderFragmentRecordingIdentity RecordingIdentity
        => _recordingIdentity ??= new RenderFragmentRecordingIdentity(this);

    /// <summary>The identity this fragment has already been settled as, if it has been.</summary>
    internal RenderFragmentRecordingIdentity? SettledRecordingIdentity => _recordingIdentity;

    /// <summary>Adopts the identity this fragment has just been compared equal to.</summary>
    internal void SettleRecordingIdentity(RenderFragmentRecordingIdentity identity)
        => _recordingIdentity = identity;

    internal ulong RecordingFlags => PackRecordingFlags();

    internal (RenderFragmentHitTestKind Kind, Rect Region, object? ContractIdentity) RecordedHitTestRule
        => _hitTest.RuleIdentity;

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

    public object? Payload { get; }

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

    /// <summary>Recreates this fragment for another request, over <paramref name="inputs"/>.</summary>
    /// <remarks>
    /// The clone carries the values this fragment was recorded with, not the ones metadata resolution later
    /// wrote into it: a fragment is rebased before any request resolves it, and reproducing a resolved value
    /// would hand the new request a bound the previous one settled. Everything the constructor derives from
    /// the inputs is derived again from the ones given here - the hit-test rule included, which is why a
    /// clone answers over the fragments it is replayed onto rather than the ones it was recorded over. A
    /// clone made over no inputs at all is therefore a faithful template for one made over real ones.
    /// </remarks>
    internal RenderFragmentReference CloneForReplay(ImmutableArray<RenderFragmentReference> inputs)
        => new(
            Kind,
            RecordedBounds,
            RecordedEffectiveScale,
            ValueCardinality,
            ContributesValuesToTarget,
            CanBeUsedAsValueInput,
            HasTargetEffects,
            HasOpaqueExternalWork,
            inputs,
            Payload,
            _hitTest,
            BoundsRequirement,
            _hasDirectSymbolicBoundsDependency);

    private long ComputeRecordingFingerprint()
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            hash = Mix(hash, (ulong)(byte)Kind);
            hash = Mix(hash, (ulong)(uint)BitConverter.SingleToInt32Bits(RecordedBounds.X));
            hash = Mix(hash, (ulong)(uint)BitConverter.SingleToInt32Bits(RecordedBounds.Y));
            hash = Mix(hash, (ulong)(uint)BitConverter.SingleToInt32Bits(RecordedBounds.Width));
            hash = Mix(hash, (ulong)(uint)BitConverter.SingleToInt32Bits(RecordedBounds.Height));
            hash = Mix(
                hash,
                RecordedEffectiveScale.IsUnbounded
                    ? ulong.MaxValue
                    : (ulong)(uint)BitConverter.SingleToInt32Bits(RecordedEffectiveScale.Value));
            hash = Mix(hash, (ulong)(byte)BoundsRequirement);
            hash = Mix(hash, (ulong)(uint)ValueCardinality.Minimum);
            hash = Mix(
                hash,
                ValueCardinality.Maximum is { } maximum ? (ulong)(uint)maximum : ulong.MaxValue);
            hash = Mix(hash, PackRecordingFlags());
            hash = Mix(hash, _hitTest.IdentityDigest);
            hash = Mix(
                hash,
                Payload is null ? 0UL : (ulong)Payload.GetType().TypeHandle.Value.ToInt64());
            hash = Mix(hash, PackObservablePayloadIdentity());
            hash = Mix(hash, (ulong)(uint)Inputs.Length);
            foreach (RenderFragmentReference input in Inputs)
                hash = Mix(hash, (ulong)input.RecordingFingerprint);
            return (long)hash;
        }
    }

    /// <summary>
    /// The part of the payload another node can read while recording, which the payload's type alone does not
    /// separate.
    /// </summary>
    /// <remarks>
    /// <see cref="TargetWriteMetadataResolver"/> reaches into an input's payload for the target extent a
    /// consumer scopes by, so two fragments that agree on everything else and disagree here are not
    /// interchangeable to whoever records above them. The rest of a payload reaches execution only, where the
    /// node that recorded it answers for it through <see cref="RenderNode.HasChanges"/>.
    /// </remarks>
    private ulong PackObservablePayloadIdentity()
    {
        switch (Payload)
        {
            case BlendRenderFragmentPayload blend:
                return 1UL + (byte)blend.BlendMode;
            case TargetCommandRenderFragmentPayload command:
                return PackRegion(command.Description.AffectedRegion);
            case TargetLayerScopeRenderFragmentPayload layer:
                return PackRegion(layer.Region);
            case TargetScopeRenderFragmentPayload scope:
                return ((ulong)(uint)scope.Description.Bounds.StructuralIdentity.GetHashCode() << 1)
                       | (scope.Description.IsValueReplayMap ? 1UL : 0UL);
            default:
                return 0;
        }
    }

    private static ulong PackRegion(TargetRegion region)
    {
        ulong hash = 1UL + (byte)region.Kind;
        if (region.Kind != TargetRegionKind.Region)
            return hash;

        Rect value = region.Value;
        unchecked
        {
            hash = (hash * 31) + (uint)BitConverter.SingleToInt32Bits(value.X);
            hash = (hash * 31) + (uint)BitConverter.SingleToInt32Bits(value.Y);
            hash = (hash * 31) + (uint)BitConverter.SingleToInt32Bits(value.Width);
            hash = (hash * 31) + (uint)BitConverter.SingleToInt32Bits(value.Height);
        }

        return hash;
    }

    private ulong PackRecordingFlags()
    {
        ulong flags = 0;
        if (ContributesValuesToTarget) flags |= 1UL << 0;
        if (CanBeUsedAsValueInput) flags |= 1UL << 1;
        if (HasTargetEffects) flags |= 1UL << 2;
        if (HasOpaqueExternalWork) flags |= 1UL << 3;
        if (HasConcreteRecordingMetadata) flags |= 1UL << 4;
        if (HasSymbolicBoundsDependency) flags |= 1UL << 5;
        if (SupportsIndependentOutputDensities) flags |= 1UL << 6;
        if (PotentiallyWritesTarget) flags |= 1UL << 7;
        if (HasSymbolicTargetWrite) flags |= 1UL << 8;
        if (_hasDirectSymbolicBoundsDependency) flags |= 1UL << 9;
        return flags;
    }

    private static ulong Mix(ulong hash, ulong value)
    {
        unchecked
        {
            const ulong Prime = 1099511628211UL;
            for (int shift = 0; shift < 64; shift += 8)
            {
                hash ^= (value >> shift) & 0xFF;
                hash *= Prime;
            }

            return hash;
        }
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
            _ => inputsWriteSymbolically,
        };
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
