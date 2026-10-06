using System.Collections.Immutable;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RegionAnalyzer
{
    private static RequiredRegion GetRootRequirement(
        RenderFragmentReference root,
        Rect finalCommitBounds,
        Rect? targetDomain)
    {
        RequiredRegion result = RequiredRegion.Empty;
        if (root.ContributesValuesToTarget)
            result = result.Union(RequiredRegion.Region(finalCommitBounds.Intersect(root.Bounds)));

        if (TargetWriteMetadataResolver.Resolve(root, targetDomain) is { } affected)
        {
            result = result.Union(
                RequiredRegion.Region(finalCommitBounds.Intersect(affected)));
        }

        return result;
    }

    private static ImmutableArray<RequiredRegion> GetInputRequirements(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement,
        Rect? targetDomain)
    {
        if (reference.Inputs.IsDefaultOrEmpty)
            return [];
        if (outputRequirement.IsEmpty)
            return RepeatInputs(RequiredRegion.Empty, reference.Inputs.Length);

        return reference.Payload switch
        {
            ShaderRenderFragmentPayload shader
                => MapUnary(reference, outputRequirement, shader.Description.Bounds),
            GeometryRenderFragmentPayload geometry
                => MapUnary(reference, outputRequirement, geometry.Description.Bounds),
            TargetScopeRenderFragmentPayload scope
                => MapTargetScope(
                    reference,
                    outputRequirement,
                    scope.Description.Bounds,
                    targetDomain),
            RawTargetScopeRenderFragmentPayload
                => FullInputs(reference),
            OpaqueRenderFragmentPayload opaque
                => MapOpaque(reference, outputRequirement, opaque.Description.Bounds),
            TargetCommandRenderFragmentPayload or RawTargetCommandRenderFragmentPayload
                => FullInputs(reference),
            FilterEffectSegmentRenderFragmentPayload effectItem
                => MapEffectItem(reference, outputRequirement, effectItem, targetDomain),
            BlendRenderFragmentPayload blend
                when BlendModeRenderNode.RequiresFullTargetRegion(blend.BlendMode)
                => MapDestructiveBlendInput(reference, outputRequirement),
            OpacityRenderFragmentPayload or BlendRenderFragmentPayload
                => MapScopedIdentityInputs(reference, outputRequirement, targetDomain),
            OpacityMaskRenderFragmentPayload
                => MapOpacityMask(reference, outputRequirement, targetDomain),
            LayerRenderFragmentPayload layer
                => MapScopeInputs(reference, outputRequirement, layer.Domain ?? reference.Bounds),
            TargetLayerScopeRenderFragmentPayload layer
                => MapScopeInputs(
                    reference,
                    outputRequirement,
                    ResolveTargetRegion(layer.Region, targetDomain)),
            _ => MapIdentityInputs(reference, outputRequirement),
        };
    }

    private static ImmutableArray<RequiredRegion> MapUnary(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement,
        RenderBoundsContract bounds)
    {
        if (reference.Inputs.Length != 1)
            throw new InvalidOperationException("A unary bounds contract requires exactly one input fragment.");
        if (bounds.RequiresFullInput)
            return [RequiredRegion.Full];
        if (outputRequirement.IsFull
            && bounds.StructuralIdentity is RenderBoundsStructuralIdentity
            {
                Kind: RenderBoundsContractKind.Identity,
            })
        {
            return [RequiredRegion.Full];
        }

        Rect requested = outputRequirement.Resolve(reference.Bounds);
        Rect required = bounds.GetRequiredInputBounds(requested);
        return [RequiredRegion.Region(required.Intersect(reference.Inputs[0].Bounds))];
    }

    private static ImmutableArray<RequiredRegion> MapTargetScope(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement,
        RenderBoundsContract bounds,
        Rect? targetDomain)
    {
        if (reference.Inputs.Length != 1)
            throw new InvalidOperationException("A target scope requires exactly one input fragment.");

        RequiredRegion required;
        if (bounds.RequiresFullInput)
        {
            required = RequiredRegion.Full;
        }
        else if (outputRequirement.IsFull
                 && bounds.StructuralIdentity is RenderBoundsStructuralIdentity
                 {
                     Kind: RenderBoundsContractKind.Identity,
                 })
        {
            required = RequiredRegion.Full;
        }
        else
        {
            Rect requested = outputRequirement.Resolve(ResolveSemanticBounds(reference, targetDomain));
            required = RequiredRegion.Region(bounds.GetRequiredInputBounds(requested));
        }

        Rect? inputTargetDomain = targetDomain is { } domain
            ? bounds.GetRequiredInputBounds(domain)
            : null;
        return [RestrictToSemanticCoverage(reference.Inputs[0], required, inputTargetDomain)];
    }

    private static ImmutableArray<RequiredRegion> MapOpaque(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement,
        OpaqueRenderBoundsContract bounds)
    {
        if (RequiresFullInputs(bounds))
            return FullInputs(reference);
        if (outputRequirement.IsFull && IsIdentityMap(bounds))
            return FullInputs(reference);

        Rect requested = outputRequirement.Resolve(reference.Bounds);
        Rect[] inputBounds = reference.Inputs.SelectToArray(static input => input.Bounds);
        IReadOnlyList<Rect> required = bounds.GetRequiredInputBounds(requested, inputBounds);
        var result = ImmutableArray.CreateBuilder<RequiredRegion>(required.Count);
        for (int index = 0; index < required.Count; index++)
        {
            result.Add(RequiredRegion.Region(required[index].Intersect(inputBounds[index])));
        }

        return result.MoveToImmutable();
    }

    private static bool RequiresFullInputs(OpaqueRenderBoundsContract bounds)
    {
        if (bounds.Kind == OpaqueRenderBoundsKind.FullInputs)
            return true;

        return bounds.StructuralIdentity is OpaqueRenderBoundsStructuralIdentity
        {
            Kind: OpaqueRenderBoundsKind.Map,
            ForwardIdentity: RenderBoundsStructuralIdentity
            {
                Kind: RenderBoundsContractKind.FullInput or RenderBoundsContractKind.CustomFullInput,
            },
        };
    }

    private static bool IsIdentityMap(OpaqueRenderBoundsContract bounds)
    {
        return bounds.StructuralIdentity is OpaqueRenderBoundsStructuralIdentity
        {
            Kind: OpaqueRenderBoundsKind.Map,
            ForwardIdentity: RenderBoundsStructuralIdentity
            {
                Kind: RenderBoundsContractKind.Identity,
            },
        };
    }

    private static ImmutableArray<RequiredRegion> MapScopedIdentityInputs(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement,
        Rect? targetDomain)
    {
        var result = ImmutableArray.CreateBuilder<RequiredRegion>(reference.Inputs.Length);
        foreach (RenderFragmentReference input in reference.Inputs)
            result.Add(RestrictToSemanticCoverage(input, outputRequirement, targetDomain));
        return result.MoveToImmutable();
    }

    private static ImmutableArray<RequiredRegion> MapDestructiveBlendInput(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement)
    {
        if (reference.Inputs.Length != 1)
        {
            throw new InvalidOperationException(
                "A destructive blend command requires exactly one source input.");
        }

        return [outputRequirement.Intersect(reference.Inputs[0].Bounds)];
    }

    private static ImmutableArray<RequiredRegion> MapEffectItem(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement,
        FilterEffectSegmentRenderFragmentPayload payload,
        Rect? targetDomain)
    {
        if (outputRequirement.IsFull
            || reference.BoundsRequirement != RenderFragmentBoundsRequirement.Finite)
        {
            return FullInputs(reference);
        }

        Rect requestedOutput = outputRequirement.Resolve(ResolveSemanticBounds(reference, targetDomain));
        if (!EffectItemSamplingSupport.TryResolveSampledInput(payload.BoundsItems, requestedOutput, out Rect requested))
            return FullInputs(reference);

        var result = ImmutableArray.CreateBuilder<RequiredRegion>(reference.Inputs.Length);
        for (int index = 0; index < reference.Inputs.Length; index++)
        {
            // A brush dependency is sampled by an opaque callback over the whole brush frame, so its
            // region cannot be narrowed by the stream's backward region.
            result.Add(index < payload.StreamInputCount
                ? RestrictToSemanticCoverage(
                    reference.Inputs[index],
                    RequiredRegion.Region(requested),
                    targetDomain)
                : RequiredRegion.Full);
        }

        return result.MoveToImmutable();
    }

    private static ImmutableArray<RequiredRegion> MapOpacityMask(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement,
        Rect? targetDomain)
    {
        var result = ImmutableArray.CreateBuilder<RequiredRegion>(reference.Inputs.Length);
        result.Add(RestrictToSemanticCoverage(reference.Inputs[0], outputRequirement, targetDomain));
        for (int index = 1; index < reference.Inputs.Length; index++)
            result.Add(RequiredRegion.Full);
        return result.MoveToImmutable();
    }

    private static RequiredRegion RestrictToSemanticCoverage(
        RenderFragmentReference input,
        RequiredRegion requirement,
        Rect? targetDomain)
    {
        RequiredRegion result = requirement.Intersect(input.Bounds);
        if (TargetWriteMetadataResolver.Resolve(input, targetDomain) is { } affected)
            result = result.Union(requirement.Intersect(affected));
        return result;
    }

    private static Rect ResolveSemanticBounds(
        RenderFragmentReference reference,
        Rect? targetDomain)
    {
        Rect result = reference.Bounds;
        if (TargetWriteMetadataResolver.Resolve(reference, targetDomain) is { } affected)
            result = result.Union(affected);
        return result;
    }

    private static ImmutableArray<RequiredRegion> MapScopeInputs(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement,
        Rect domain)
    {
        if (domain.Width == 0 || domain.Height == 0)
        {
            return RepeatInputs(RequiredRegion.Empty, reference.Inputs.Length);
        }

        var result = ImmutableArray.CreateBuilder<RequiredRegion>(reference.Inputs.Length);
        foreach (RenderFragmentReference input in reference.Inputs)
        {
            RequiredRegion inputRequirement = RequiredRegion.Empty;
            if (input.ContributesValuesToTarget)
            {
                inputRequirement = inputRequirement.Union(
                    outputRequirement.Intersect(input.Bounds.Intersect(domain)));
            }

            if (TargetWriteMetadataResolver.Resolve(input, domain) is { } affected)
            {
                inputRequirement = inputRequirement.Union(
                    outputRequirement.Intersect(affected.Intersect(domain)));
            }

            result.Add(inputRequirement);
        }

        return result.MoveToImmutable();
    }

    private static ImmutableArray<RequiredRegion> MapIdentityInputs(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement)
    {
        var result = ImmutableArray.CreateBuilder<RequiredRegion>(reference.Inputs.Length);
        foreach (RenderFragmentReference input in reference.Inputs)
        {
            result.Add(outputRequirement.IsFull
                ? RequiredRegion.Full
                : outputRequirement.Intersect(input.Bounds));
        }
        return result.MoveToImmutable();
    }

    private static ImmutableArray<RequiredRegion> FullInputs(RenderFragmentReference reference)
        => RepeatInputs(RequiredRegion.Full, reference.Inputs.Length);

    /// <summary>Builds one requirement per input, all the same.</summary>
    /// <remarks>
    /// The propagation loop calls this once per fragment per pass and runs at least twice, so filling a
    /// sized builder is preferred to the iterator <c>Repeat</c> would allocate for the range to walk.
    /// </remarks>
    private static ImmutableArray<RequiredRegion> RepeatInputs(RequiredRegion region, int count)
    {
        if (count == 0)
            return [];

        ImmutableArray<RequiredRegion>.Builder builder = ImmutableArray.CreateBuilder<RequiredRegion>(count);
        for (int index = 0; index < count; index++)
            builder.Add(region);
        return builder.MoveToImmutable();
    }

    private static RequiredRegion? GetTargetAccessRequirement(
        RenderFragmentReference reference,
        RequiredRegion outputRequirement,
        Rect? targetDomain)
    {
        return reference.Payload switch
        {
            TargetCaptureRenderFragmentPayload capture
                => MapTargetAccess(outputRequirement, capture.Description.SourceRegion, targetDomain),
            BuiltInBackdropCaptureRenderFragmentPayload capture
                => MapTargetAccess(outputRequirement, capture.Description.SourceRegion, targetDomain),
            TargetCommandRenderFragmentPayload command
                when command.Description.Access == TargetAccess.Readback
                => MapTargetAccess(RequiredRegion.Full, command.Description.AffectedRegion, targetDomain),
            TargetCommandRenderFragmentPayload command
                => MapTargetAccess(outputRequirement, command.Description.AffectedRegion, targetDomain),
            BlendRenderFragmentPayload blend
                when BlendModeRenderNode.RequiresFullTargetRegion(blend.BlendMode)
                => MapTargetAccess(outputRequirement, TargetRegion.Full, targetDomain),
            RawTargetCommandRenderFragmentPayload
                => outputRequirement.IsEmpty ? RequiredRegion.Empty : RequiredRegion.Full,
            RawTargetScopeRenderFragmentPayload
                => outputRequirement.IsEmpty ? RequiredRegion.Empty : RequiredRegion.Full,
            TargetLayerScopeRenderFragmentPayload layer
                => MapTargetAccess(outputRequirement, layer.Region, targetDomain),
            LayerRenderFragmentPayload layer
                => outputRequirement.Intersect(layer.Domain ?? reference.Bounds),
            _ => null,
        };
    }

    private static RequiredRegion MapTargetAccess(
        RequiredRegion requirement,
        TargetRegion access,
        Rect? targetDomain)
    {
        if (requirement.IsEmpty || access.Kind == TargetRegionKind.Empty)
            return RequiredRegion.Empty;
        if (access.Kind == TargetRegionKind.Full && requirement.IsFull)
            return RequiredRegion.Full;

        Rect domain = ResolveTargetRegion(access, targetDomain);
        return requirement.IsFull
            ? RequiredRegion.Region(domain)
            : requirement.Intersect(domain);
    }

    private static Rect ResolveTargetRegion(TargetRegion region, Rect? targetDomain)
    {
        return region.Kind switch
        {
            TargetRegionKind.Empty => Rect.Empty,
            TargetRegionKind.Region => region.Value,
            TargetRegionKind.Full when targetDomain is { } domain => domain,
            TargetRegionKind.Full => throw new RenderTargetDomainRequiredException(
                "A target-less request with Full target access requires a finite TargetDomain."),
            _ => throw new InvalidOperationException("The target region is uninitialized."),
        };
    }

    private static bool UnionRequirement(
        Dictionary<RenderFragmentReference, RequiredRegion> requirements,
        RenderFragmentReference reference,
        RequiredRegion requirement)
    {
        RequiredRegion previous = GetRequirement(requirements, reference);
        RequiredRegion combined = previous.Union(requirement);
        requirements[reference] = combined;
        return combined != previous;
    }

    private static RequiredRegion GetRequirement(
        Dictionary<RenderFragmentReference, RequiredRegion> requirements,
        RenderFragmentReference reference)
        => requirements.TryGetValue(reference, out RequiredRegion requirement)
            ? requirement
            : RequiredRegion.Empty;
}
