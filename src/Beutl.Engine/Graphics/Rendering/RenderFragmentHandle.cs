using System.Collections.Immutable;
using System.Runtime.CompilerServices;

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

/// <summary>How a fragment answers a hit test, stated so that it can be re-evaluated over any inputs.</summary>
internal enum RenderFragmentHitTestKind : byte
{
    /// <summary>Never hits.</summary>
    None,

    /// <summary>Hits everywhere inside the fragment's own bounds.</summary>
    Bounds,

    /// <summary>Hits everywhere inside a fixed region the fragment carries.</summary>
    Region,

    /// <summary>Hits wherever any input hits.</summary>
    Inputs,

    /// <summary>Hits wherever an input hits inside a fixed region the fragment carries.</summary>
    RegionAndInputs,

    /// <summary>Hits where an author-declared contract says, read over the fragment's bounds and inputs.</summary>
    Contract,
}

/// <summary>What a recorded fragment answers a hit test with, as a rule rather than a bound delegate.</summary>
/// <remarks>
/// A delegate built while recording closes over the fragments that request held. Replay recreates a fragment
/// over the inputs of the request it is replayed into, so such a delegate would answer for a graph that has
/// ended. A rule names its inputs instead of capturing them, which lets replay rebase the hit test the same
/// way it rebases everything else - and lets <see cref="RenderFragmentReference.RecordingFingerprint"/> speak
/// for the hit test, because a rule has an identity a digest can read.
/// </remarks>
internal readonly struct RenderFragmentHitTest
{
    private readonly Rect _region;
    private readonly RenderHitTestContract _contract;
    private readonly IReadOnlyList<RenderResourceBinding>? _resources;

    private RenderFragmentHitTest(
        RenderFragmentHitTestKind kind,
        Rect region,
        RenderHitTestContract contract,
        IReadOnlyList<RenderResourceBinding>? resources)
    {
        Kind = kind;
        _region = region;
        _contract = contract;
        _resources = resources;
    }

    public RenderFragmentHitTestKind Kind { get; }

    public static RenderFragmentHitTest None => default;

    public static RenderFragmentHitTest Bounds { get; } =
        new(RenderFragmentHitTestKind.Bounds, default, default, null);

    public static RenderFragmentHitTest Inputs { get; } =
        new(RenderFragmentHitTestKind.Inputs, default, default, null);

    public static RenderFragmentHitTest Region(Rect region)
        => new(RenderFragmentHitTestKind.Region, region, default, null);

    public static RenderFragmentHitTest RegionAndInputs(Rect region)
        => new(RenderFragmentHitTestKind.RegionAndInputs, region, default, null);

    public static RenderFragmentHitTest FromContract(
        RenderHitTestContract contract,
        IReadOnlyList<RenderResourceBinding>? resources)
        => new(RenderFragmentHitTestKind.Contract, default, contract, resources);

    public bool Evaluate(Rect bounds, ImmutableArray<RenderFragmentReference> inputs, Point point)
        => Kind switch
        {
            RenderFragmentHitTestKind.Bounds => bounds.Contains(point),
            RenderFragmentHitTestKind.Region => _region.Contains(point),
            RenderFragmentHitTestKind.Inputs => AnyInput(inputs, point),
            RenderFragmentHitTestKind.RegionAndInputs
                => _region.Contains(point) && AnyInput(inputs, point),
            RenderFragmentHitTestKind.Contract
                => _contract.Evaluate(bounds, CreateInputViews(inputs), _resources ?? [], point),
            _ => false,
        };

    /// <summary>A digest of which rule this is, ignoring the state an author-declared contract reads.</summary>
    /// <remarks>
    /// A contract's structural identity is which callback answers, not what it answers over: a contract built
    /// from a resource or from per-recording state keeps one identity while that state moves. The state
    /// belongs to the node that recorded it and is answered for by
    /// <see cref="RenderNode.HasChanges"/>; a consumer that only forwards the hit test reads the live one
    /// through <see cref="RenderFragmentReference.Inputs"/> either way.
    /// </remarks>
    public ulong IdentityDigest
    {
        get
        {
            unchecked
            {
                ulong hash = Combine(14695981039346656037UL, (byte)Kind);
                switch (Kind)
                {
                    case RenderFragmentHitTestKind.Region:
                    case RenderFragmentHitTestKind.RegionAndInputs:
                        hash = Combine(hash, (uint)BitConverter.SingleToInt32Bits(_region.X));
                        hash = Combine(hash, (uint)BitConverter.SingleToInt32Bits(_region.Y));
                        hash = Combine(hash, (uint)BitConverter.SingleToInt32Bits(_region.Width));
                        hash = Combine(hash, (uint)BitConverter.SingleToInt32Bits(_region.Height));
                        break;
                    case RenderFragmentHitTestKind.Contract:
                        hash = Combine(hash, (uint)ContractIdentityHash());
                        break;
                }

                return hash;
            }
        }
    }

    private int ContractIdentityHash()
    {
        object identity = _contract.StructuralIdentity;
        // A boxed contract kind or bounds identity answers for its value; a callback answers only for which
        // object it is, because two closures over equal state are not the same declaration.
        return identity is ValueType
            ? identity.GetHashCode()
            : RuntimeHelpers.GetHashCode(identity);
    }

    private static ulong Combine(ulong hash, ulong value)
    {
        unchecked
        {
            return (hash ^ value) * 1099511628211UL;
        }
    }

    private static bool AnyInput(ImmutableArray<RenderFragmentReference> inputs, Point point)
    {
        foreach (RenderFragmentReference input in inputs)
        {
            if (input.HitTest(point))
                return true;
        }

        return false;
    }

    private static RenderHitTestInput[] CreateInputViews(ImmutableArray<RenderFragmentReference> inputs)
    {
        if (inputs.Length == 0)
            return [];

        var views = new RenderHitTestInput[inputs.Length];
        for (int index = 0; index < inputs.Length; index++)
            views[index] = new RenderHitTestInput(inputs[index].Bounds, inputs[index].HitTest);
        return views;
    }
}

internal sealed class RenderFragmentReference
{
    private readonly bool _hasDirectSymbolicBoundsDependency;

    // Holds the recorded rule until planning lowers it, and a fragment is captured for replay and
    // fingerprinted before planning begins, so both of those read what recording stated. Nothing may clone or
    // digest a fragment after ApplyResolvedMetadata has run on it.
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

    public RenderFragmentId? Id { get; set; }

    public ImmutableArray<RenderValueId> ValueIds { get; set; } = [];

    public bool AllowsFanOut => CanBeUsedAsValueInput;

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
                if (RequiresFullTargetRegion(reference))
                {
                    affectedBounds = null;
                    return false;
                }
                return TryResolveFiniteReplay(reference, out affectedBounds);
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
                when RequiresFullTargetRegion(reference)
                => ResolveRegion(TargetRegion.Full, targetDomain),
            RenderFragmentKind.Blend
                or RenderFragmentKind.Opacity
                or RenderFragmentKind.OpacityMask
                => ResolveReplayBounds(reference, targetDomain),
            _ => null,
        };
    }

    private static bool RequiresFullTargetRegion(RenderFragmentReference reference)
    {
        return BlendModeRenderNode.RequiresFullTargetRegion(
            ((BlendRenderFragmentPayload)reference.Payload!).BlendMode);
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
            TargetRegionKind.Full => throw new RenderTargetDomainRequiredException(
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

/// <summary>
/// Answers, for every <see cref="RenderFragmentKind"/>, the device pixel grid a fragment replays its inputs
/// onto.
/// </summary>
/// <remarks>
/// The unmatched arm answers <see cref="RenderDeviceGridMapping.Remapped"/>, so a form whose target state the
/// planner cannot analyse — an opaque external barrier, or a kind added after this switch was written — costs
/// upstream cache reuse instead of serving a phase-dependent raster at the wrong grid phase.
/// </remarks>
internal static class RenderFragmentDeviceGrid
{
    public static RenderDeviceGridMapping ResolveMapping(RenderFragmentReference reference)
        => reference.Kind switch
        {
            RenderFragmentKind.TargetScope
                => ((TargetScopeRenderFragmentPayload)reference.Payload!).Description.DeviceGridMapping,
            // Every kind whose replay, composition, or value materialization is engine-owned and free of
            // author-supplied target state.
            RenderFragmentKind.ContributeValues
                or RenderFragmentKind.Opacity
                or RenderFragmentKind.Blend
                or RenderFragmentKind.OpacityMask
                or RenderFragmentKind.Shader
                or RenderFragmentKind.Geometry
                or RenderFragmentKind.OpaqueSource
                or RenderFragmentKind.OpaqueMap
                or RenderFragmentKind.OpaqueCombine
                or RenderFragmentKind.OpaqueExpand
                or RenderFragmentKind.FilterEffectSegment
                or RenderFragmentKind.MaterializedInput
                or RenderFragmentKind.TargetCapture
                or RenderFragmentKind.Layer
                or RenderFragmentKind.TargetLayerScope
                or RenderFragmentKind.TargetCommand
                or RenderFragmentKind.BuiltInBackdropCapture
                => RenderDeviceGridMapping.Preserved,
            _ => RenderDeviceGridMapping.Remapped,
        };
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
    FilterEffectSegment,
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
