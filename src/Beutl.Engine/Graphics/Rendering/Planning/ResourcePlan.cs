using System.Collections.Immutable;

using Beutl.Media;

namespace Beutl.Graphics.Rendering;

internal readonly record struct ResourcePlanValueId(int Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

internal readonly record struct ResourcePlanSlotId(int Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

internal sealed class ResourcePlanRequirement
{
    public ResourcePlanRequirement(
        ResourcePlanValueId valueId,
        PixelSize deviceSize,
        int acquisitionPosition,
        IEnumerable<int> consumerPositions,
        bool transferToCache = false)
    {
        if (valueId.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(valueId));
        if (deviceSize.Width <= 0 || deviceSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(deviceSize));
        if (acquisitionPosition < 0)
            throw new ArgumentOutOfRangeException(nameof(acquisitionPosition));
        ArgumentNullException.ThrowIfNull(consumerPositions);

        ImmutableArray<int> consumers = [.. consumerPositions.Order()];
        if (consumers.Any(position => position < acquisitionPosition))
        {
            throw new ArgumentException(
                "A resource cannot be consumed before it is acquired.",
                nameof(consumerPositions));
        }

        ValueId = valueId;
        DeviceSize = deviceSize;
        AcquisitionPosition = acquisitionPosition;
        ConsumerPositions = consumers;
        LastUsePosition = consumers.IsDefaultOrEmpty ? acquisitionPosition : consumers[^1];
        TransferToCache = transferToCache;
    }

    public ResourcePlanValueId ValueId { get; }

    public PixelSize DeviceSize { get; }

    public int AcquisitionPosition { get; }

    public ImmutableArray<int> ConsumerPositions { get; }

    public int LastUsePosition { get; }

    public bool TransferToCache { get; }
}

internal sealed class ResourcePlanAllocation
{
    internal ResourcePlanAllocation(
        ResourcePlanRequirement requirement,
        ResourcePlanSlotId slotId)
    {
        Requirement = requirement;
        SlotId = slotId;
    }

    public ResourcePlanRequirement Requirement { get; }

    public ResourcePlanValueId ValueId => Requirement.ValueId;

    public ResourcePlanSlotId SlotId { get; }

    public PixelSize DeviceSize => Requirement.DeviceSize;

    public int FirstUsePosition => Requirement.AcquisitionPosition;

    public int LastUsePosition => Requirement.LastUsePosition;

    public bool TransferToCache => Requirement.TransferToCache;
}

internal sealed class ResourcePlan
{
    private readonly Dictionary<ResourcePlanValueId, ResourcePlanAllocation> _byValue;
    private readonly Dictionary<int, ImmutableArray<ResourcePlanAllocation>> _acquisitions;
    private readonly Dictionary<int, ImmutableArray<ResourcePlanAllocation>> _discharges;

    private ResourcePlan(
        ImmutableArray<ResourcePlanAllocation> allocations,
        int physicalSlotCount,
        int peakLiveIntermediates,
        ImmutableArray<int> positions)
    {
        Allocations = allocations;
        PhysicalSlotCount = physicalSlotCount;
        PeakLiveIntermediates = peakLiveIntermediates;
        Positions = positions;
        _byValue = allocations.ToDictionary(static allocation => allocation.ValueId);
        _acquisitions = allocations
            .GroupBy(static allocation => allocation.FirstUsePosition)
            .ToDictionary(
                static group => group.Key,
                static group => group.OrderBy(item => item.ValueId.Value).ToImmutableArray());
        _discharges = allocations
            .GroupBy(static allocation => allocation.LastUsePosition)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .OrderByDescending(item => item.FirstUsePosition)
                    .ThenByDescending(item => item.ValueId.Value)
                    .ToImmutableArray());
    }

    public ImmutableArray<ResourcePlanAllocation> Allocations { get; }

    public ImmutableArray<int> Positions { get; }

    public int PhysicalSlotCount { get; }

    public int PeakLiveIntermediates { get; }

    public static ResourcePlan Create(IEnumerable<ResourcePlanRequirement> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ResourcePlanRequirement[] ordered = requirements
            .OrderBy(static requirement => requirement.AcquisitionPosition)
            .ThenBy(static requirement => requirement.ValueId.Value)
            .ToArray();
        if (ordered.Select(static item => item.ValueId).Distinct().Count() != ordered.Length)
            throw new ArgumentException("Resource-plan value identities must be unique.", nameof(requirements));

        var slots = new List<PlanningSlot>();
        var allocations = ImmutableArray.CreateBuilder<ResourcePlanAllocation>(ordered.Length);
        foreach (ResourcePlanRequirement requirement in ordered)
        {
            PlanningSlot? selected = slots
                .Where(slot => !slot.Retired
                    && slot.Size == requirement.DeviceSize
                    && slot.AvailableAfter < requirement.AcquisitionPosition)
                .OrderByDescending(static slot => slot.AvailableAfter)
                .ThenBy(static slot => slot.Id.Value)
                .FirstOrDefault();
            if (selected is null)
            {
                selected = new PlanningSlot(
                    new ResourcePlanSlotId(slots.Count + 1),
                    requirement.DeviceSize);
                slots.Add(selected);
            }

            allocations.Add(new ResourcePlanAllocation(requirement, selected.Id));
            selected.AvailableAfter = requirement.LastUsePosition;
            selected.Retired = requirement.TransferToCache;
        }

        int peakLive = ComputePeakLive(ordered);
        ImmutableArray<int> positions = [.. ordered
            .SelectMany(static item => new[] { item.AcquisitionPosition, item.LastUsePosition })
            .Distinct()
            .Order()];
        return new ResourcePlan(allocations.MoveToImmutable(), slots.Count, peakLive, positions);
    }

    public static ResourcePlanUseSchedule CreateUseSchedule(
        IReadOnlyList<RenderFragmentReference> roots,
        IReadOnlySet<RenderFragmentId>? terminalFragmentIds = null)
        => ResourcePlanUseSchedule.Create(roots, terminalFragmentIds);

    public ResourcePlanAllocation GetAllocation(ResourcePlanValueId valueId)
        => _byValue.TryGetValue(valueId, out ResourcePlanAllocation? allocation)
            ? allocation
            : throw new KeyNotFoundException($"Resource-plan value {valueId} is not scheduled.");

    public ImmutableArray<ResourcePlanAllocation> GetAcquisitions(int position)
        => _acquisitions.TryGetValue(position, out ImmutableArray<ResourcePlanAllocation> result)
            ? result
            : [];

    public ImmutableArray<ResourcePlanAllocation> GetDischarges(int position)
        => _discharges.TryGetValue(position, out ImmutableArray<ResourcePlanAllocation> result)
            ? result
            : [];

    public ResourcePlanExecution BeginExecution(RenderTargetPoolRequest targets)
        => new(this, targets);

    private static int ComputePeakLive(IEnumerable<ResourcePlanRequirement> requirements)
    {
        var events = new SortedDictionary<int, (int Starts, int Ends)>();
        foreach (ResourcePlanRequirement requirement in requirements)
        {
            events.TryGetValue(requirement.AcquisitionPosition, out (int Starts, int Ends) first);
            events[requirement.AcquisitionPosition] = (first.Starts + 1, first.Ends);
            events.TryGetValue(requirement.LastUsePosition, out (int Starts, int Ends) last);
            events[requirement.LastUsePosition] = (last.Starts, last.Ends + 1);
        }

        int live = 0;
        int peak = 0;
        foreach ((int starts, int ends) in events.Values)
        {
            live += starts;
            peak = Math.Max(peak, live);
            live -= ends;
        }

        return peak;
    }

    private sealed class PlanningSlot(ResourcePlanSlotId id, PixelSize size)
    {
        public ResourcePlanSlotId Id { get; } = id;

        public PixelSize Size { get; } = size;

        public int AvailableAfter { get; set; } = -1;

        public bool Retired { get; set; }
    }
}

internal readonly record struct ResourcePlanFragmentLifetime(
    RenderFragmentReference Fragment,
    int AcquisitionPosition,
    ImmutableArray<int> ConsumerPositions)
{
    public int LastUsePosition
        => ConsumerPositions.IsDefaultOrEmpty
            ? AcquisitionPosition
            : ConsumerPositions[^1];
}

/// <summary>
/// Structural value-use schedule for a recorded request. Runtime-discovered streams share their producer
/// interval; their exact target sizes remain selected by the pool when the callback publishes each value.
/// </summary>
internal sealed class ResourcePlanUseSchedule
{
    private ResourcePlanUseSchedule(ImmutableArray<ResourcePlanFragmentLifetime> lifetimes)
    {
        Lifetimes = lifetimes;
    }

    public ImmutableArray<ResourcePlanFragmentLifetime> Lifetimes { get; }

    public ResourcePlanUseTracker BeginExecution()
        => new(Lifetimes);

    internal static ResourcePlanUseSchedule Create(
        IReadOnlyList<RenderFragmentReference> roots,
        IReadOnlySet<RenderFragmentId>? terminalFragmentIds = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        terminalFragmentIds ??= new HashSet<RenderFragmentId>();
        var ordered = new List<RenderFragmentReference>();
        var visiting = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<RenderFragmentReference>(ReferenceEqualityComparer.Instance);
        foreach (RenderFragmentReference root in roots)
        {
            ArgumentNullException.ThrowIfNull(root);
            Visit(root, terminalFragmentIds, visiting, visited, ordered);
        }

        var positions = new Dictionary<RenderFragmentReference, int>(ReferenceEqualityComparer.Instance);
        var consumers = new Dictionary<RenderFragmentReference, List<int>>(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < ordered.Count; index++)
        {
            RenderFragmentReference fragment = ordered[index];
            positions.Add(fragment, index);
            consumers.Add(fragment, []);
        }

        for (int index = 0; index < ordered.Count; index++)
        {
            RenderFragmentReference fragment = ordered[index];
            if (fragment.Id is { } id && terminalFragmentIds.Contains(id))
                continue;
            foreach (RenderFragmentReference input in fragment.ExecutionInputs)
                consumers[input].Add(index);
        }

        for (int index = 0; index < roots.Count; index++)
            consumers[roots[index]].Add(checked(ordered.Count + index));

        return new ResourcePlanUseSchedule(
        [
            .. ordered.Select(fragment => new ResourcePlanFragmentLifetime(
                fragment,
                positions[fragment],
                [.. consumers[fragment].Order()])),
        ]);

        static void Visit(
            RenderFragmentReference fragment,
            IReadOnlySet<RenderFragmentId> terminalFragmentIds,
            HashSet<RenderFragmentReference> visiting,
            HashSet<RenderFragmentReference> visited,
            List<RenderFragmentReference> ordered)
        {
            if (visited.Contains(fragment))
                return;
            if (!visiting.Add(fragment))
                throw new InvalidOperationException("The resource-use graph contains a fragment cycle.");

            if (fragment.Id is not { } id || !terminalFragmentIds.Contains(id))
            {
                foreach (RenderFragmentReference input in fragment.ExecutionInputs)
                    Visit(input, terminalFragmentIds, visiting, visited, ordered);
            }

            visiting.Remove(fragment);
            visited.Add(fragment);
            ordered.Add(fragment);
        }
    }
}

internal sealed class ResourcePlanUseTracker
{
    private readonly Dictionary<RenderFragmentReference, int> _remainingUses;

    internal ResourcePlanUseTracker(ImmutableArray<ResourcePlanFragmentLifetime> lifetimes)
    {
        _remainingUses = new Dictionary<RenderFragmentReference, int>(
            lifetimes.Length,
            ReferenceEqualityComparer.Instance);
        foreach (ResourcePlanFragmentLifetime lifetime in lifetimes)
            _remainingUses.Add(lifetime.Fragment, lifetime.ConsumerPositions.Length);
    }

    /// <summary>Completes one authored edge/root use and returns true at the producer's last use.</summary>
    public bool CompleteUse(RenderFragmentReference fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        if (!_remainingUses.TryGetValue(fragment, out int remaining) || remaining <= 0)
        {
            throw new InvalidOperationException(
                "A render fragment was consumed more times than its resource plan declares.");
        }

        remaining--;
        _remainingUses[fragment] = remaining;
        return remaining == 0;
    }

    public int GetRemainingUseCount(RenderFragmentReference fragment)
    {
        ArgumentNullException.ThrowIfNull(fragment);
        return _remainingUses.TryGetValue(fragment, out int remaining)
            ? remaining
            : throw new InvalidOperationException(
                "A render fragment is not part of the resource-use schedule.");
    }
}

internal readonly record struct ResourcePlanCacheTransfer(
    ResourcePlanValueId ValueId,
    RenderTarget Target);

internal sealed class ResourcePlanExecution : IDisposable
{
    private readonly ResourcePlan _plan;
    private readonly RenderTargetPoolRequest _targets;
    private readonly Dictionary<ResourcePlanValueId, PooledRenderTargetLease> _live = [];
    private readonly List<ResourcePlanCacheTransfer> _transfers = [];
    private int? _activePosition;
    private int _lastCompletedPosition = -1;
    private int _peakLive;
    private bool _disposed;

    internal ResourcePlanExecution(ResourcePlan plan, RenderTargetPoolRequest targets)
    {
        _plan = plan;
        _targets = targets ?? throw new ArgumentNullException(nameof(targets));
        ObjectDisposedException.ThrowIf(targets.IsDisposed, targets);
    }

    public IReadOnlyList<ResourcePlanCacheTransfer> CacheTransfers => _transfers;

    public int PeakLiveIntermediates => _peakLive;

    public void BeginPosition(int position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activePosition.HasValue)
            throw new InvalidOperationException("The current resource-plan position has not completed.");
        if (position <= _lastCompletedPosition)
            throw new InvalidOperationException("Resource-plan positions must execute in increasing order.");

        foreach (ResourcePlanAllocation allocation in _plan.GetAcquisitions(position))
        {
            PooledRenderTargetLease lease = _targets.Acquire(allocation.DeviceSize);
            if (!_live.TryAdd(allocation.ValueId, lease))
            {
                lease.Dispose();
                throw new InvalidOperationException(
                    $"Resource-plan value {allocation.ValueId} was acquired more than once.");
            }
        }

        _peakLive = Math.Max(_peakLive, _live.Count);
        _activePosition = position;
    }

    public RenderTarget GetTarget(ResourcePlanValueId valueId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _live.TryGetValue(valueId, out PooledRenderTargetLease? lease)
            ? lease.Target
            : throw new InvalidOperationException($"Resource-plan value {valueId} is not live.");
    }

    public void CompletePosition(
        int position,
        Func<ResourcePlanAllocation, RenderTarget, bool>? acceptCacheTransfer = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_activePosition != position)
            throw new InvalidOperationException("Only the active resource-plan position can complete.");

        foreach (ResourcePlanAllocation allocation in _plan.GetDischarges(position))
        {
            if (!_live.TryGetValue(allocation.ValueId, out PooledRenderTargetLease? lease))
            {
                throw new InvalidOperationException(
                    $"Resource-plan value {allocation.ValueId} is not live at its last use.");
            }

            if (allocation.TransferToCache
                && acceptCacheTransfer?.Invoke(allocation, lease.Target) == true)
            {
                RenderTarget transferred = lease.TransferToAcceptedCache();
                _transfers.Add(new ResourcePlanCacheTransfer(allocation.ValueId, transferred));
            }
            else
            {
                lease.Dispose();
            }

            _live.Remove(allocation.ValueId);
        }

        _lastCompletedPosition = position;
        _activePosition = null;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (PooledRenderTargetLease lease in _live.Values.Reverse())
        {
            if (lease.State == PooledRenderTargetLeaseState.Leased)
                lease.Dispose();
        }
        _live.Clear();
    }
}
