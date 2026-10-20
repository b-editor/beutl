using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Everything <see cref="RenderFragmentReference.RecordingFingerprint"/> digests, kept so that a digest match
/// can be settled by comparison instead of trusted.
/// </summary>
/// <remarks>
/// Identity comparison must stay correct when the 64-bit fingerprint collides. The identity keeps no fragment
/// or payload, so a retained recording pins nothing, and it reads what recording stated, so it is taken before
/// <see cref="RenderFragmentReference.ApplyResolvedMetadata"/> runs.
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
