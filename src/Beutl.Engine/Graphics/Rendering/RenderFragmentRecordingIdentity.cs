using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Everything <see cref="RenderFragmentReference.RecordingFingerprint"/> digests, kept so that a digest match
/// can be settled by comparison instead of trusted.
/// </summary>
/// <remarks>
/// <para>
/// FR-033 requires identity comparison to stay correct under hash collisions, and the fingerprint is 64 bits
/// standing for a whole input cone - two of whose members, a target region's rectangle and a bounds
/// contract's identity, it can only hash. The digest stays what the per-node path compares first and rejects
/// on; this settles the pairs that survive it.
/// </para>
/// <para>
/// It is detached by construction: it names its inputs' identities rather than the fragments, and it keeps a
/// payload's identity object rather than the payload, so a retained recording does not pin the graph or the
/// resources of the request that made it. It reads what recording stated, so - like cloning and digesting -
/// it has to be taken before <see cref="RenderFragmentReference.ApplyResolvedMetadata"/> runs.
/// </para>
/// <para>
/// A fragment compared equal to an identity adopts it. Recording settles the deepest fragments first, so an
/// ancestor comparing over them reaches a reference comparison one level down instead of walking the cone
/// again, which is what keeps the settled comparison linear in the graph rather than in every node's cone.
/// </para>
/// </remarks>
internal sealed class RenderFragmentRecordingIdentity
{
    private readonly RenderFragmentKind _kind;
    private readonly Rect _bounds;
    private readonly EffectiveScale _effectiveScale;
    private readonly RenderFragmentBoundsRequirement _boundsRequirement;
    private readonly RenderValueCardinality _cardinality;
    private readonly ulong _flags;
    private readonly RenderFragmentHitTestKind _hitTestKind;
    private readonly Rect _hitTestRegion;
    private readonly object? _hitTestContractIdentity;
    private readonly Type? _payloadType;
    private readonly BlendMode _blendMode;
    private readonly TargetRegionKind _regionKind;
    private readonly Rect _regionValue;
    private readonly object? _payloadBoundsIdentity;
    private readonly bool _isValueReplayMap;
    private readonly RenderFragmentRecordingIdentity[] _inputs;

    internal RenderFragmentRecordingIdentity(RenderFragmentReference reference)
    {
        _kind = reference.Kind;
        _bounds = reference.RecordedBounds;
        _effectiveScale = reference.RecordedEffectiveScale;
        _boundsRequirement = reference.BoundsRequirement;
        _cardinality = reference.ValueCardinality;
        _flags = reference.RecordingFlags;
        (_hitTestKind, _hitTestRegion, _hitTestContractIdentity) = reference.RecordedHitTestRule;
        _payloadType = reference.Payload?.GetType();
        switch (reference.Payload)
        {
            case BlendRenderFragmentPayload blend:
                _blendMode = blend.BlendMode;
                break;
            case TargetCommandRenderFragmentPayload command:
                _regionKind = command.Description.AffectedRegion.Kind;
                _regionValue = RegionValue(command.Description.AffectedRegion);
                break;
            case TargetLayerScopeRenderFragmentPayload layer:
                _regionKind = layer.Region.Kind;
                _regionValue = RegionValue(layer.Region);
                break;
            case TargetScopeRenderFragmentPayload scope:
                _payloadBoundsIdentity = scope.Description.Bounds.StructuralIdentity;
                _isValueReplayMap = scope.Description.IsValueReplayMap;
                break;
        }

        ImmutableArray<RenderFragmentReference> inputs = reference.Inputs;
        _inputs = inputs.Length == 0 ? [] : new RenderFragmentRecordingIdentity[inputs.Length];
        for (int index = 0; index < inputs.Length; index++)
            _inputs[index] = inputs[index].RecordingIdentity;
    }

    /// <summary>Whether <paramref name="reference"/> has the structure this identity was taken from.</summary>
    public bool Matches(RenderFragmentReference reference)
    {
        if (ReferenceEquals(reference.SettledRecordingIdentity, this))
            return true;

        if (_kind != reference.Kind
            || !_bounds.Equals(reference.RecordedBounds)
            || !_effectiveScale.Equals(reference.RecordedEffectiveScale)
            || _boundsRequirement != reference.BoundsRequirement
            || !_cardinality.Equals(reference.ValueCardinality)
            || _flags != reference.RecordingFlags
            || _payloadType != reference.Payload?.GetType()
            || _inputs.Length != reference.Inputs.Length
            || !MatchesHitTest(reference.RecordedHitTestRule)
            || !MatchesPayload(reference.Payload))
        {
            return false;
        }

        for (int index = 0; index < _inputs.Length; index++)
        {
            if (!_inputs[index].Matches(reference.Inputs[index]))
                return false;
        }

        reference.SettleRecordingIdentity(this);
        return true;
    }

    private static Rect RegionValue(TargetRegion region)
        => region.Kind == TargetRegionKind.Region ? region.Value : default;

    private bool MatchesHitTest(
        (RenderFragmentHitTestKind Kind, Rect Region, object? ContractIdentity) rule)
        => _hitTestKind == rule.Kind
           && _hitTestRegion.Equals(rule.Region)
           && RenderFragmentHitTest.SameStructuralIdentity(_hitTestContractIdentity, rule.ContractIdentity);

    private bool MatchesPayload(object? payload)
        => payload switch
        {
            BlendRenderFragmentPayload blend => _blendMode == blend.BlendMode,
            TargetCommandRenderFragmentPayload command
                => MatchesRegion(command.Description.AffectedRegion),
            TargetLayerScopeRenderFragmentPayload layer => MatchesRegion(layer.Region),
            TargetScopeRenderFragmentPayload scope
                => _isValueReplayMap == scope.Description.IsValueReplayMap
                   && RenderFragmentHitTest.SameStructuralIdentity(
                       _payloadBoundsIdentity,
                       scope.Description.Bounds.StructuralIdentity),
            _ => true,
        };

    private bool MatchesRegion(TargetRegion region)
        => _regionKind == region.Kind && _regionValue.Equals(RegionValue(region));
}
