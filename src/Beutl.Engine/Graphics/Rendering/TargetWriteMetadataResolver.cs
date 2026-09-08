namespace Beutl.Graphics.Rendering;

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
