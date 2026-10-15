namespace Beutl.Graphics.Rendering;

internal static class TargetWriteMetadataResolver
{
    public static bool TryResolveFinite(
        RenderFragmentReference reference,
        out Rect? affectedBounds)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return TryResolve(reference, targetDomain: null, recorded: true, out affectedBounds);
    }

    public static Rect? Resolve(
        RenderFragmentReference reference,
        Rect? targetDomain)
    {
        ArgumentNullException.ThrowIfNull(reference);
        TryResolve(reference, targetDomain, recorded: false, out Rect? affectedBounds);
        return affectedBounds;
    }

    // While recording, the walk reads the recorded metadata and answers false for a write it cannot bound.
    // When planning, it reads the resolved bounds, and a Full write without a domain throws.
    private static bool TryResolve(
        RenderFragmentReference reference,
        Rect? targetDomain,
        bool recorded,
        out Rect? affectedBounds)
    {
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
                    targetDomain,
                    recorded,
                    out affectedBounds);
            case RenderFragmentKind.RawTargetCommand:
                return TryResolveRegion(TargetRegion.Full, targetDomain, recorded, out affectedBounds);
            case RenderFragmentKind.TargetLayerScope:
                return TryResolveRegion(
                    ((TargetLayerScopeRenderFragmentPayload)reference.Payload!).Region,
                    targetDomain,
                    recorded,
                    out affectedBounds);
            case RenderFragmentKind.TargetScope:
                return TryResolveTargetScope(reference, targetDomain, recorded, out affectedBounds);
            case RenderFragmentKind.Blend when RequiresFullTargetRegion(reference):
                return TryResolveRegion(TargetRegion.Full, targetDomain, recorded, out affectedBounds);
            case RenderFragmentKind.Blend:
            case RenderFragmentKind.Opacity:
            case RenderFragmentKind.OpacityMask:
                return TryResolveReplay(reference, targetDomain, recorded, out affectedBounds);
            default:
                affectedBounds = null;
                return !recorded;
        }
    }

    private static bool RequiresFullTargetRegion(RenderFragmentReference reference)
    {
        return BlendModeRenderNode.RequiresFullTargetRegion(
            ((BlendRenderFragmentPayload)reference.Payload!).BlendMode);
    }

    private static bool TryResolveTargetScope(
        RenderFragmentReference reference,
        Rect? targetDomain,
        bool recorded,
        out Rect? affectedBounds)
    {
        var payload = (TargetScopeRenderFragmentPayload)reference.Payload!;
        Rect? localDomain = targetDomain is { } domain
            ? payload.Description.Bounds.GetRequiredInputBounds(domain)
            : null;
        if (!TryResolveReplay(reference, localDomain, recorded, out Rect? replayBounds))
        {
            affectedBounds = null;
            return false;
        }

        affectedBounds = replayBounds is { } bounds
            ? payload.Description.Bounds.TransformBounds(bounds)
            : null;
        return true;
    }

    private static bool TryResolveReplay(
        RenderFragmentReference reference,
        Rect? targetDomain,
        bool recorded,
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
                if (recorded && !input.HasConcreteRecordingMetadata)
                {
                    affectedBounds = null;
                    return false;
                }

                result = result.Union(recorded ? input.RecordedBounds : input.Bounds);
                hasBounds = true;
            }

            if (!TryResolve(input, targetDomain, recorded, out Rect? inputAffectedBounds))
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
        bool recorded,
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
            case TargetRegionKind.Full when recorded:
                affectedBounds = null;
                return false;
            case TargetRegionKind.Full:
                throw new RenderTargetDomainRequiredException(
                    "A target-less request with a Full target write requires a finite TargetDomain.");
            default:
                throw new InvalidOperationException("The target region is uninitialized.");
        }
    }
}
