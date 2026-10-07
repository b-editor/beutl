using Beutl.Media;

using SkiaSharp;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderTargetPool
{
    private bool TryTakeAvailable(PixelSize size, RenderTargetPixelFormat pixelFormat, out TargetSlot? slot)
    {
        if (_availableBuckets.TryGetValue((size, pixelFormat), out LinkedList<TargetSlot>? bucket)
            && bucket.Last is { } node)
        {
            slot = node.Value;
            RemoveAvailable(slot);
            return true;
        }

        slot = null;
        return false;
    }

    private void AddAvailable(TargetSlot slot)
    {
        if (!_availableBuckets.TryGetValue((slot.Size, slot.PixelFormat), out LinkedList<TargetSlot>? bucket))
        {
            bucket = [];
            _availableBuckets.Add((slot.Size, slot.PixelFormat), bucket);
        }

        slot.BucketNode = bucket.AddLast(slot);
        slot.LruNode = _availableLru.AddLast(slot);
        _retainedBytes = checked(_retainedBytes + slot.ByteSize);
    }

    private void RemoveAvailable(TargetSlot slot)
    {
        if (slot.BucketNode is { } bucketNode
            && _availableBuckets.TryGetValue((slot.Size, slot.PixelFormat), out LinkedList<TargetSlot>? bucket))
        {
            bucket.Remove(bucketNode);
            if (bucket.Count == 0)
                _availableBuckets.Remove((slot.Size, slot.PixelFormat));
        }

        if (slot.LruNode is { } lruNode)
            _availableLru.Remove(lruNode);

        if (slot.BucketNode is not null || slot.LruNode is not null)
            _retainedBytes -= slot.ByteSize;
        slot.BucketNode = null;
        slot.LruNode = null;
    }

    private void TrimIdle(RenderTargetLeaseSession request)
    {
        while (_availableLru.First is { } node
               && _requestEpoch - node.Value.LastUsedEpoch > _options.MaximumIdleRequests)
        {
            Evict(node.Value, request, failures: null);
        }
    }

    private void TrimToByteBudget(RenderTargetLeaseSession request)
    {
        while (_retainedBytes > _options.MaximumRetainedBytes
               && _availableLru.First is { } node)
        {
            Evict(node.Value, request, failures: null);
        }
    }

    private void EvictAllAvailable(RenderTargetLeaseSession? request, List<Exception>? failures)
    {
        while (_availableLru.First is { } node)
            Evict(node.Value, request, failures);
    }

    private void Evict(
        TargetSlot slot,
        RenderTargetLeaseSession? request,
        List<Exception>? failures,
        RenderTargetLease? failedLease = null)
    {
        if (!_ownedSlots.Contains(slot))
            return;

        RenderTargetLease? liveLease = slot.ActiveLease;
        if (liveLease is not null)
        {
            if (failedLease is not null && !ReferenceEquals(liveLease, failedLease))
                throw new InvalidOperationException("A stale lease cannot evict another active lease.");
            liveLease.State = RenderTargetLeaseState.Evicted;
            slot.ActiveLease = null;
            _leasedTargets--;
        }
        else if (failedLease is
        {
            State: RenderTargetLeaseState.Leased
                         or RenderTargetLeaseState.ReleaseFailed
                         or RenderTargetLeaseState.Deferred,
        })
        {
            failedLease.State = RenderTargetLeaseState.Evicted;
            _leasedTargets--;
        }
        RemoveAvailable(slot);
        RemoveOwnedSlot(slot);
        _evictions++;
        try
        {
            slot.Target.Dispose();
        }
        catch (Exception ex)
        {
            if (request is not null)
                request.RecordCleanupFailure(ex);
            else
                failures?.Add(ex);
        }
    }

    private void RemoveOwnedSlot(TargetSlot slot)
    {
        if (!_ownedSlots.Remove(slot))
            return;

        RemoveAvailable(slot);
        _knownTargets.Remove(slot.Target);
        _knownSurfaces.Remove(slot.Surface);
        _ownedBytes -= slot.ByteSize;
    }

    internal sealed class TargetSlot(
        RenderTarget target,
        SKSurface surface,
        PixelSize size,
        long byteSize,
        RenderTargetPixelFormat pixelFormat)
    {
        public RenderTarget Target { get; } = target;

        public SKSurface Surface { get; } = surface;

        public PixelSize Size { get; } = size;

        public RenderTargetPixelFormat PixelFormat { get; } = pixelFormat;

        public long ByteSize { get; } = byteSize;

        public long LastUsedEpoch { get; set; }

        public RenderTargetLease? ActiveLease { get; set; }

        public LinkedListNode<TargetSlot>? BucketNode { get; set; }

        public LinkedListNode<TargetSlot>? LruNode { get; set; }
    }
}
