using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering;

internal static class TargetDependencyLowerer
{
    public static TargetDependencyPlan Lower(
        ImmutableArray<RenderFragmentReference> roots,
        Rect? rootDomain = null)
    {
        var builder = new Builder();
        TargetScopeId rootScope = builder.CreateScope(
            parentId: null,
            owner: null,
            resolvedDomain: rootDomain);
        foreach (RenderFragmentReference root in roots)
            builder.LowerRoot(root, rootScope);
        return builder.Build();
    }

    private sealed class Builder
    {
        private readonly List<TargetDependencyStep> _steps = [];
        private readonly List<TargetScopePlan> _scopes = [];
        private readonly Dictionary<TargetScopeId, TargetTokenId> _currentTokens = [];
        private readonly HashSet<RenderFragmentReference> _scheduledEffects =
            new(ReferenceEqualityComparer.Instance);
        private int _nextScopeId;
        private int _nextTokenId;

        public TargetScopeId CreateScope(
            TargetScopeId? parentId,
            RenderFragmentReference? owner,
            Rect? resolvedDomain,
            bool inheritParentToken = false,
            bool isOrderOnly = false)
        {
            var scopeId = new TargetScopeId(++_nextScopeId);
            TargetTokenId token = inheritParentToken && parentId is { } parent
                ? _currentTokens[parent]
                : new TargetTokenId(++_nextTokenId);
            _currentTokens.Add(scopeId, token);
            _scopes.Add(new TargetScopePlan(
                scopeId,
                parentId,
                owner?.Id,
                token,
                resolvedDomain,
                isOrderOnly));
            return scopeId;
        }

        public void LowerRoot(
            RenderFragmentReference reference,
            TargetScopeId scopeId,
            bool compositeOutput = true)
        {
            switch (reference.Kind)
            {
                case RenderFragmentKind.Layer:
                    LowerFiniteLayer(reference, scopeId, compositeOutput);
                    return;
                case RenderFragmentKind.TargetLayerScope:
                    LowerTargetLayerScope(reference, scopeId);
                    return;
                case RenderFragmentKind.TargetCapture:
                case RenderFragmentKind.BuiltInBackdropCapture:
                    LowerCapture(reference, scopeId);
                    if (compositeOutput && reference.ContributesValuesToTarget)
                        AddStep(reference, scopeId, TargetDependencyKind.Composite, FirstInputValue(reference), null);
                    return;
                case RenderFragmentKind.TargetCommand:
                case RenderFragmentKind.RawTargetCommand:
                    ValidateCommandDomain(reference, scopeId);
                    LowerCommand(reference, scopeId);
                    return;
                case RenderFragmentKind.TargetScope:
                    LowerScopeWrapper(reference, scopeId, compositeOutput);
                    return;
                case RenderFragmentKind.RawTargetScope:
                    ValidateFullDomain(reference, scopeId);
                    LowerScopeWrapper(reference, scopeId, compositeOutput);
                    return;
                case RenderFragmentKind.ContributeValues:
                    LowerDependencies(reference, scopeId);
                    if (compositeOutput)
                    {
                        AddStep(
                            reference,
                            scopeId,
                            TargetDependencyKind.Composite,
                            FirstInputValue(reference),
                            null);
                    }
                    return;
                case RenderFragmentKind.Blend
                    when RequiresFullTargetRegion(reference):
                    LowerDestructiveBlend(reference, scopeId);
                    return;
                case RenderFragmentKind.Blend:
                case RenderFragmentKind.Opacity:
                    LowerScopeWrapper(reference, scopeId, compositeOutput);
                    return;
                case RenderFragmentKind.OpacityMask:
                    LowerOpacityMask(reference, scopeId, compositeOutput);
                    return;
                default:
                    LowerDependencies(reference, scopeId);
                    if (compositeOutput && reference.ContributesValuesToTarget)
                        AddStep(reference, scopeId, TargetDependencyKind.Composite, FirstValue(reference), null);
                    return;
            }
        }

        public TargetDependencyPlan Build() => new([.. _steps], [.. _scopes]);

        private static bool RequiresFullTargetRegion(RenderFragmentReference reference)
        {
            return BlendModeRenderNode.RequiresFullTargetRegion(
                ((BlendRenderFragmentPayload)reference.Payload!).BlendMode);
        }

        private void LowerDestructiveBlend(
            RenderFragmentReference reference,
            TargetScopeId scopeId)
        {
            if (!_scheduledEffects.Add(reference))
                return;

            ValidateFullDomain(reference, scopeId);
            LowerDependencies(reference, scopeId);
            AddStep(
                reference,
                scopeId,
                TargetDependencyKind.Command,
                FirstInputValue(reference),
                null);
        }

        private void LowerFiniteLayer(
            RenderFragmentReference reference,
            TargetScopeId parentScope,
            bool compositeOutput)
        {
            if (!_scheduledEffects.Add(reference))
                return;

            Rect domain = ((LayerRenderFragmentPayload)reference.Payload!).Domain
                ?? reference.Bounds;
            TargetScopeId childScope = CreateScope(
                parentScope,
                reference,
                domain);
            foreach (RenderFragmentReference input in reference.Inputs)
                LowerRoot(input, childScope);

            if (compositeOutput && reference.ContributesValuesToTarget)
            {
                AddStep(
                    reference,
                    parentScope,
                    TargetDependencyKind.ScopeComposite,
                    FirstValue(reference),
                    null);
            }
        }

        private void LowerTargetLayerScope(
            RenderFragmentReference reference,
            TargetScopeId parentScope)
        {
            if (!_scheduledEffects.Add(reference))
                return;

            TargetRegion region = ((TargetLayerScopeRenderFragmentPayload)reference.Payload!).Region;
            Rect domain = ResolveRegion(region, GetDomain(parentScope), reference);
            bool isOrderOnly = region.Kind == TargetRegionKind.Empty;
            TargetScopeId childScope = CreateScope(
                parentScope,
                reference,
                domain,
                isOrderOnly: isOrderOnly);
            if (isOrderOnly)
                return;

            foreach (RenderFragmentReference input in reference.Inputs)
                LowerRoot(input, childScope);

            AddStep(
                reference,
                parentScope,
                TargetDependencyKind.ScopeComposite,
                FirstValue(reference),
                null);
        }

        private void LowerScopeWrapper(
            RenderFragmentReference reference,
            TargetScopeId scopeId,
            bool compositeOutput)
        {
            if (!_scheduledEffects.Add(reference))
                return;

            Rect? authoredDomain = MapDomainIntoScope(reference, GetDomain(scopeId));
            TargetScopeId authoredScope = CreateScope(
                scopeId,
                reference,
                authoredDomain,
                inheritParentToken: true);
            bool childHasEffects = false;
            foreach (RenderFragmentReference input in reference.Inputs)
            {
                if (input.HasTargetEffects)
                {
                    childHasEffects = true;
                    LowerRoot(input, authoredScope, compositeOutput);
                }
            }

            if (compositeOutput && reference.ContributesValuesToTarget && !childHasEffects)
            {
                AddStep(reference, authoredScope, TargetDependencyKind.Composite, FirstValue(reference), null);
            }

            _currentTokens[scopeId] = _currentTokens[authoredScope];
        }

        private void LowerOpacityMask(
            RenderFragmentReference reference,
            TargetScopeId scopeId,
            bool compositeOutput)
        {
            if (!_scheduledEffects.Add(reference))
                return;

            for (int i = 1; i < reference.Inputs.Length; i++)
            {
                RenderFragmentReference dependency = reference.Inputs[i];
                if (dependency.HasTargetEffects)
                    LowerRoot(dependency, scopeId, compositeOutput: false);
            }

            Rect? authoredDomain = MapDomainIntoScope(reference, GetDomain(scopeId));
            TargetScopeId authoredScope = CreateScope(
                scopeId,
                reference,
                authoredDomain,
                inheritParentToken: true);
            bool childHasEffects = false;
            if (!reference.Inputs.IsDefaultOrEmpty)
            {
                RenderFragmentReference primary = reference.Inputs[0];
                if (primary.HasTargetEffects)
                {
                    childHasEffects = true;
                    LowerRoot(primary, authoredScope, compositeOutput);
                }
            }

            if (compositeOutput && reference.ContributesValuesToTarget && !childHasEffects)
            {
                AddStep(reference, authoredScope, TargetDependencyKind.Composite, FirstValue(reference), null);
            }

            _currentTokens[scopeId] = _currentTokens[authoredScope];
        }

        private void ValidateCaptureDomain(
            RenderFragmentReference reference,
            TargetScopeId scopeId)
        {
            Rect? targetDomain = GetDomain(scopeId);
            TargetRegion region = reference.Payload switch
            {
                TargetCaptureRenderFragmentPayload capture => capture.Description.SourceRegion,
                BuiltInBackdropCaptureRenderFragmentPayload capture => capture.Description.SourceRegion,
                _ => throw new InvalidOperationException("The target-capture payload is invalid."),
            };
            Rect resolvedSourceRegion = ResolveRegion(region, targetDomain, reference);
            if (reference.Payload is TargetCaptureRenderFragmentPayload publicCapture)
            {
                publicCapture.Description.ValidateResolvedBounds(
                    resolvedSourceRegion,
                    targetDomain ?? resolvedSourceRegion);
            }
        }

        private void ValidateCommandDomain(
            RenderFragmentReference reference,
            TargetScopeId scopeId)
        {
            TargetRegion region = reference.Payload switch
            {
                TargetCommandRenderFragmentPayload command => command.Description.AffectedRegion,
                RawTargetCommandRenderFragmentPayload => TargetRegion.Full,
                _ => throw new InvalidOperationException("The target-command payload is invalid."),
            };
            _ = ResolveRegion(region, GetDomain(scopeId), reference);
        }

        private void ValidateFullDomain(
            RenderFragmentReference reference,
            TargetScopeId scopeId)
            => _ = ResolveRegion(TargetRegion.Full, GetDomain(scopeId), reference);

        private void LowerCommand(RenderFragmentReference reference, TargetScopeId scopeId)
        {
            if (!_scheduledEffects.Add(reference))
                return;
            LowerDependencies(reference, scopeId);
            AddStep(reference, scopeId, TargetDependencyKind.Command, FirstInputValue(reference), null);
        }

        private void LowerCapture(RenderFragmentReference reference, TargetScopeId scopeId)
        {
            if (!_scheduledEffects.Add(reference))
                return;
            ValidateCaptureDomain(reference, scopeId);
            RenderValueId? capturedValue = FirstValue(reference);
            AddStep(reference, scopeId, TargetDependencyKind.Capture, capturedValue, capturedValue);
        }

        private void LowerDependencies(
            RenderFragmentReference reference,
            TargetScopeId scopeId)
        {
            foreach (RenderFragmentReference input in reference.Inputs)
            {
                if (!input.HasTargetEffects)
                    continue;

                LowerRoot(input, scopeId, compositeOutput: false);
            }
        }

        private void AddStep(
            RenderFragmentReference reference,
            TargetScopeId scopeId,
            TargetDependencyKind kind,
            RenderValueId? targetReadValueId,
            RenderValueId? producedValueId)
        {
            RenderFragmentId fragmentId = reference.Id
                ?? throw new InvalidOperationException("A target dependency refers to an uncommitted fragment.");
            TargetTokenId input = _currentTokens[scopeId];
            var output = new TargetTokenId(++_nextTokenId);
            _currentTokens[scopeId] = output;
            _steps.Add(new TargetDependencyStep(
                fragmentId,
                scopeId,
                input,
                output,
                targetReadValueId,
                producedValueId,
                kind));
        }

        private static RenderValueId? FirstValue(RenderFragmentReference reference)
            => reference.ValueIds.IsDefaultOrEmpty ? null : reference.ValueIds[0];

        private static RenderValueId? FirstInputValue(RenderFragmentReference reference)
        {
            foreach (RenderFragmentReference input in reference.Inputs)
            {
                if (!input.ValueIds.IsDefaultOrEmpty)
                    return input.ValueIds[0];
            }

            return null;
        }

        private Rect? GetDomain(TargetScopeId scopeId)
        {
            int index = scopeId.Value - 1;
            if ((uint)index >= (uint)_scopes.Count || _scopes[index].Id != scopeId)
            {
                throw new InvalidOperationException("The target scope ID does not identify a created scope.");
            }

            return _scopes[index].ResolvedDomain;
        }

        private static Rect? MapDomainIntoScope(
            RenderFragmentReference reference,
            Rect? parentDomain)
        {
            if (parentDomain is not { } domain)
                return null;

            return reference.Payload switch
            {
                TargetScopeRenderFragmentPayload scope
                    => scope.Description.Bounds.GetRequiredInputBounds(domain),
                RawTargetScopeRenderFragmentPayload scope
                    => scope.Description.Bounds.GetRequiredInputBounds(domain),
                _ => domain,
            };
        }

        private static Rect ResolveRegion(
            TargetRegion region,
            Rect? ownerDomain,
            RenderFragmentReference owner)
        {
            return region.Kind switch
            {
                TargetRegionKind.Empty => Rect.Empty,
                TargetRegionKind.Region => region.Value,
                TargetRegionKind.Full when ownerDomain is { } domain => domain,
                TargetRegionKind.Full => throw new RenderTargetDomainRequiredException(
                    $"A reachable Full target access on {owner.Kind} requires a finite owning target domain."),
                _ => throw new InvalidOperationException("The target region is uninitialized."),
            };
        }
    }
}
