using Beutl.Media;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// Builds the statically knowable part of one request family's render-target lifetime schedule.
/// Runtime-sized opaque outputs and callback-dependent geometry crops remain guarded by the same
/// request-family ledger when they acquire their targets.
/// </summary>
internal static class RenderAllocationPreflight
{
    public static void Validate(
        CompiledRenderRequest request,
        RenderTargetLeaseSession targets,
        PixelSize? rootTargetSize = null,
        Vector deviceGridOffset = default,
        DirectRenderTargetGeometry? directOutputTarget = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(targets);
        ObjectDisposedException.ThrowIf(request.IsDisposed, request);

        var pending = new List<PendingLifetime>();
        int position = 0;
        if (rootTargetSize is { } rootSize)
        {
            pending.Add(new PendingLifetime(
                Fragment: null,
                IsPreviewOptional: false,
                rootSize,
                position++,
                LastUsePosition: null));
        }

        CollectRequest(
            request,
            isNested: false,
            deviceGridOffset,
            directOutputTarget,
            pending,
            ref position);

        int familyEnd = Math.Max(position, 1);
        ResolvedLifetime[] resolved =
        [
            .. pending.Select(item => new ResolvedLifetime(
                item.Fragment,
                item.IsPreviewOptional,
                item.DeviceSize,
                item.AcquisitionPosition,
                item.LastUsePosition ?? familyEnd,
                targets.GetValidatedByteSize(item.DeviceSize))),
        ];
        HashSet<RenderFragmentReference> plannedDrops = SelectPreviewDrops(resolved, targets);
        PlannedRenderTargetLifetime[] lifetimes =
        [
            .. resolved
                .Where(item => item.Fragment is null || !plannedDrops.Contains(item.Fragment))
                .Select(static item => new PlannedRenderTargetLifetime(
                    item.DeviceSize,
                    item.AcquisitionPosition,
                    item.LastUsePosition)),
        ];
        targets.ValidatePlannedAllocations(lifetimes);
        ApplyPreviewPlans(request, plannedDrops);
    }

    private static void CollectRequest(
        CompiledRenderRequest request,
        bool isNested,
        Vector deviceGridOffset,
        DirectRenderTargetGeometry? inheritedDirectOutputTarget,
        ICollection<PendingLifetime> pending,
        ref int position)
    {
        bool ownsNestedTarget = isNested
                                && (request.Measurement.HasContributingValues
                                    || request.Measurement.HasTargetEffects);
        DirectRenderTargetGeometry? directOutputTarget = inheritedDirectOutputTarget;
        if (ownsNestedTarget)
        {
            Rect bounds = request.Request.Options.TargetDomain
                ?? throw new InvalidOperationException(
                    "A separate-target nested request requires a finite target domain.");
            PixelRect deviceBounds = PixelRect.FromRect(
                bounds,
                request.Request.Options.OutputScale);
            PixelSize deviceSize = deviceBounds.Size;
            pending.Add(new PendingLifetime(
                Fragment: null,
                IsPreviewOptional: false,
                deviceSize,
                position++,
                LastUsePosition: null));
            directOutputTarget = DirectRenderTargetGeometry.FromRasterBounds(
                deviceBounds.ToRect(request.Request.Options.OutputScale),
                request.Request.Options.OutputScale);
        }

        foreach (CompiledRenderRequest nested in request.NestedRequests)
        {
            // A nested target establishes a new device grid at its exact device-bound origin.
            CollectRequest(
                nested,
                isNested: true,
                default,
                directOutputTarget,
                pending,
                ref position);
        }

        HashSet<RenderFragmentId> terminalCacheHits =
        [
            .. request.CacheResolution.Hits
                .Where(static hit => !hit.Verify)
                .Select(static hit => hit.OriginalProducerId),
        ];
        ResourcePlanUseSchedule schedule = ResourcePlanUseSchedule.Create(
            request.Roots,
            terminalCacheHits);
        int localStart = position++;
        int localLength = 1;
        foreach (ResourcePlanFragmentLifetime lifetime in schedule.Lifetimes)
        {
            localLength = Math.Max(localLength, lifetime.LastUsePosition + 1);
            if (!request.MaterializedFragments.Contains(lifetime.Fragment)
                || CanUseProvenDirectRootOutput(
                    request,
                    lifetime,
                    directOutputTarget))
            {
                continue;
            }

            if (!TryResolveKnownDeviceSize(
                    request,
                    lifetime.Fragment,
                    deviceGridOffset,
                    terminalCacheHits,
                    out PixelSize deviceSize))
            {
                continue;
            }

            int acquisitionPosition = checked(localStart + lifetime.AcquisitionPosition);
            int lastUsePosition = checked(localStart + lifetime.LastUsePosition);
            bool isPreviewOptional = request.Request.Options.Intent == RenderIntent.Preview
                                     && request.PreviewDropEligibleMaterializations.Contains(lifetime.Fragment);
            pending.Add(new PendingLifetime(
                lifetime.Fragment,
                isPreviewOptional,
                deviceSize,
                acquisitionPosition,
                lastUsePosition));

            if (lifetime.Fragment.Id is { } fragmentId)
            {
                int cacheCopies = request.CacheResolution.MissCaptures.Count(
                    capture => capture.ProducerId == fragmentId);
                long pixels = (long)deviceSize.Width * deviceSize.Height;
                if (!request.Request.Options.CachePolicy.Rules.Match(pixels))
                    cacheCopies = 0;
                for (int index = 0; index < cacheCopies; index++)
                {
                    // Cache captures are copied immediately after the producer and retained until
                    // atomic family publication succeeds or rejects them.
                    pending.Add(new PendingLifetime(
                        lifetime.Fragment,
                        isPreviewOptional,
                        deviceSize,
                        acquisitionPosition,
                        LastUsePosition: null));
                }
            }
        }

        position = checked(localStart + localLength);
    }

    private static bool CanUseProvenDirectRootOutput(
        CompiledRenderRequest request,
        ResourcePlanFragmentLifetime lifetime,
        DirectRenderTargetGeometry? destination)
    {
        RenderFragmentReference fragment = lifetime.Fragment;
        return destination is { } directTarget
               && lifetime.ConsumerPositions.Length == 1
               && request.Roots.Any(root => ReferenceEquals(root, fragment))
               && fragment.ContributesValuesToTarget
               && fragment.Id is { } id
               && request.CacheResolution.Hits.All(hit => hit.OriginalProducerId != id)
               && request.CacheResolution.MissCaptures.All(capture => capture.ProducerId != id)
               && request.ExecutionPlan.TryGetMembership(fragment, out ExecutionIslandMembership membership)
               && membership.IsTerminal
               && membership.ShaderRun is { } run
               && DirectShaderRunPlanner.TryResolve(
                   fragment,
                   run,
                   request.Regions,
                   directTarget,
                   out _);
    }

    private static bool TryResolveKnownDeviceSize(
        CompiledRenderRequest request,
        RenderFragmentReference fragment,
        Vector deviceGridOffset,
        IReadOnlySet<RenderFragmentId> terminalCacheHits,
        out PixelSize deviceSize)
    {
        deviceSize = default;
        if (!fragment.ValueCardinality.Equals(RenderValueCardinality.Single)
            || fragment.Id is not { } fragmentId
            || terminalCacheHits.Contains(fragmentId))
        {
            return false;
        }

        if (request.ExecutionPlan.TryGetMembership(fragment, out ExecutionIslandMembership membership)
            && membership.ShaderRun is { } run
            && !ReferenceEquals(run.Output, fragment))
        {
            return false;
        }

        Rect allocationBounds;
        bool rasterApron = false;
        switch (fragment.Kind)
        {
            case RenderFragmentKind.Shader:
                allocationBounds = request.Regions
                    .GetFragmentRequirement(fragment)
                    .Resolve(fragment.Bounds);
                break;
            case RenderFragmentKind.Opacity:
            case RenderFragmentKind.OpacityMask:
                allocationBounds = fragment.Bounds;
                break;
            case RenderFragmentKind.Layer:
                allocationBounds = ((LayerRenderFragmentPayload)fragment.Payload!).Domain
                    ?? fragment.Bounds;
                break;
            case RenderFragmentKind.TargetScope
                when ((TargetScopeRenderFragmentPayload)fragment.Payload!).Description.IsValueReplayMap:
                allocationBounds = request.Regions
                    .GetFragmentRequirement(fragment)
                    .Resolve(fragment.Bounds);
                rasterApron = true;
                break;
            case RenderFragmentKind.TargetCapture:
                {
                    TargetCaptureDescription description =
                        ((TargetCaptureRenderFragmentPayload)fragment.Payload!).Description;
                    if (description.Scale.PreservesTargetSupply)
                        return false;
                    allocationBounds = description.Bounds;
                    break;
                }
            case RenderFragmentKind.BuiltInBackdropCapture:
                {
                    TargetCaptureDescription description =
                        ((BuiltInBackdropCaptureRenderFragmentPayload)fragment.Payload!).Description;
                    if (description.Scale.PreservesTargetSupply)
                        return false;
                    allocationBounds = fragment.Bounds;
                    break;
                }
            default:
                // Opaque callbacks can publish a runtime-sized stream, Geometry can crop after the
                // callback, and target replay scopes select their active domain during execution.
                return false;
        }

        if (allocationBounds.Width == 0 || allocationBounds.Height == 0)
            return false;

        float density;
        if (!fragment.EffectiveScale.IsUnbounded)
        {
            density = fragment.EffectiveScale.Value;
        }
        else if (request.MaterializationDemands.TryGetValue(fragment, out EffectiveScale demand))
        {
            density = demand.Value;
        }
        else
        {
            return false;
        }

        density = RenderMaterializationDensityPolicy.Clamp(fragment, density);
        Rect completeBounds = fragment.Kind == RenderFragmentKind.Layer
            ? allocationBounds
            : fragment.Bounds;
        Rect alignedBounds = completeBounds.Translate(deviceGridOffset);
        density = rasterApron
            ? RenderScaleUtilities.ClampWorkingScaleToRasterApronBudget(alignedBounds, density)
            : RenderScaleUtilities.ClampWorkingScaleToExactBufferBudget(alignedBounds, density);

        PixelRect physicalBounds = PixelRect.FromRect(
            allocationBounds.Translate(deviceGridOffset),
            density);
        if (rasterApron)
            physicalBounds = RenderScaleUtilities.AddRasterApron(physicalBounds);
        if (physicalBounds.Width <= 0 || physicalBounds.Height <= 0)
            return false;

        deviceSize = physicalBounds.Size;
        return true;
    }

    private static HashSet<RenderFragmentReference> SelectPreviewDrops(
        IReadOnlyList<ResolvedLifetime> lifetimes,
        RenderTargetLeaseSession targets)
    {
        var dropped = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        RenderAllocationBudget budget = targets.AllocationBudget;
        int[] positions =
        [
            .. lifetimes
                .SelectMany(static item => new[] { item.AcquisitionPosition, item.LastUsePosition })
                .Distinct()
                .Order(),
        ];

        while (true)
        {
            bool selectedDrop = false;
            foreach (int position in positions)
            {
                ResolvedLifetime[] active =
                [
                    .. lifetimes.Where(item =>
                        (item.Fragment is null || !dropped.Contains(item.Fragment))
                        && item.AcquisitionPosition <= position
                        && item.LastUsePosition >= position),
                ];
                long activeBytes = targets.LiveBytes;
                bool byteOverflow = false;
                foreach (ResolvedLifetime lifetime in active)
                {
                    if (lifetime.ByteSize > budget.MaximumLiveBytes - activeBytes)
                    {
                        byteOverflow = true;
                        break;
                    }

                    activeBytes += lifetime.ByteSize;
                }

                bool targetOverflow = active.Length > budget.MaximumLiveTargets - targets.LiveTargets;
                if (!byteOverflow && !targetOverflow)
                    continue;

                PreviewDropCandidate? candidate = active
                    .Where(static item => item.IsPreviewOptional && item.Fragment is not null)
                    .GroupBy(
                        static item => item.Fragment!,
                        (IEqualityComparer<RenderFragmentReference>)ReferenceEqualityComparer.Instance)
                    .Select(group => new PreviewDropCandidate(
                        group.Key,
                        group.Aggregate(0L, static (sum, item) => SaturatingAdd(sum, item.ByteSize)),
                        group.Count()))
                    .OrderByDescending(static item => item.ActiveBytes)
                    .ThenByDescending(static item => item.ActiveTargets)
                    .ThenBy(static item => item.Fragment.Id?.Value ?? long.MaxValue)
                    .FirstOrDefault();
                if (candidate is null)
                    return dropped;

                dropped.Add(candidate.Fragment);
                selectedDrop = true;
                break;
            }

            if (!selectedDrop)
                return dropped;
        }
    }

    private static long SaturatingAdd(long left, long right)
        => left > long.MaxValue - right ? long.MaxValue : left + right;

    private static void ApplyPreviewPlans(
        CompiledRenderRequest root,
        IReadOnlySet<RenderFragmentReference> dropped)
    {
        var pending = new Stack<CompiledRenderRequest>();
        pending.Push(root);
        while (pending.TryPop(out CompiledRenderRequest? request))
        {
            request.ApplyPreviewAllocationPlan(
                request.PreviewDropEligibleMaterializations.Where(dropped.Contains));
            foreach (CompiledRenderRequest nested in request.NestedRequests)
                pending.Push(nested);
        }
    }

    private readonly record struct PendingLifetime(
        RenderFragmentReference? Fragment,
        bool IsPreviewOptional,
        PixelSize DeviceSize,
        int AcquisitionPosition,
        int? LastUsePosition);

    private readonly record struct ResolvedLifetime(
        RenderFragmentReference? Fragment,
        bool IsPreviewOptional,
        PixelSize DeviceSize,
        int AcquisitionPosition,
        int LastUsePosition,
        long ByteSize);

    private sealed record PreviewDropCandidate(
        RenderFragmentReference Fragment,
        long ActiveBytes,
        int ActiveTargets);
}
