using Beutl.Graphics.Rendering.Cache;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class RenderRequestExecutionState
    {
        private bool IsCacheCaptureValue(MaterializedRenderValue value)
            => _cacheCaptureValues?.Contains(value) == true;

        private void AddCacheCaptureValue(MaterializedRenderValue value)
            => (_cacheCaptureValues ??= new(ReferenceEqualityComparer.Instance)).Add(value);

        private void RemoveCacheCaptureValue(MaterializedRenderValue value)
            => _cacheCaptureValues?.Remove(value);

        private void ClearCacheCaptureValues()
            => _cacheCaptureValues = null;

        private bool TryMaterializeCacheHit(
            RenderFragmentReference fragment,
            out IReadOnlyList<MaterializedRenderValue>? values)
        {
            if (fragment.Id is not { } id
                || !_cacheResolution.TryGetHit(id, out RenderCacheDecision hit))
            {
                values = null;
                return false;
            }

            if (hit.HitEntry!.Payload is not RenderNodeCachedOutput cachedOutput)
            {
                throw new InvalidOperationException(
                    "A selected render-cache hit does not contain a node-cache output payload.");
            }

            var acquired = new List<MaterializedRenderValue>(cachedOutput.Values.Count);
            bool supportsIndependentOutputDensities = fragment.SupportsIndependentOutputDensities;
            try
            {
                foreach (RenderNodeCachedValue cached in cachedOutput.Values)
                {
                    if (cached.EffectiveScale.IsUnbounded
                        || (!supportsIndependentOutputDensities
                            && !MatchesPlannedDensity(cached.EffectiveScale.Value, hit.Identity!.Density)))
                    {
                        throw new InvalidOperationException(
                            "A render-cache hit payload does not match its planned materialization density.");
                    }

                    MaterializedRenderValue value = CreateOwnedShallowCopy(
                        cached.Target,
                        cached.Bounds,
                        cached.EffectiveScale,
                        cached.DeviceBounds,
                        cached.DeviceGridOffset,
                        completeBounds: cached.CompleteBounds);
                    _ownedValues.Add(value);
                    acquired.Add(value);
                }
            }
            catch
            {
                foreach (MaterializedRenderValue value in acquired)
                    ReleaseUnpublished(value);
                throw;
            }

            values = acquired;
            return true;
        }

        private void StageCacheCaptures(
            RenderFragmentReference fragment,
            IReadOnlyList<MaterializedRenderValue> values)
        {
            if (PreviewAllocationDropObserved)
                return;
            if (fragment.Id is not { } id)
                return;
            ReadOnlySpan<int> missDecisionIndices = _cacheResolution.GetMissCaptureDecisionIndices(id);
            if (missDecisionIndices.IsEmpty)
                return;

            bool supportsIndependentOutputDensities = fragment.SupportsIndependentOutputDensities;
            long actualPixels = SumDevicePixels(values);

            foreach (int decisionIndex in missDecisionIndices)
            {
                RenderCacheDecision miss = _cacheResolution.Decisions[decisionIndex];
                if (!_options.CachePolicy.Rules.Match(actualPixels))
                {
                    _cacheCaptures[decisionIndex] = s_suppressedCacheCapture;
                    continue;
                }

                var captures = new List<MaterializedRenderValue>(values.Count);
                bool dropped = false;
                try
                {
                    foreach (MaterializedRenderValue value in values)
                    {
                        if (!supportsIndependentOutputDensities
                            && !MatchesPlannedDensity(value.EffectiveScale.Value, miss.Identity!.Density))
                        {
                            throw new InvalidOperationException(
                                "A render-cache capture does not match its planned materialization density.");
                        }

                        MaterializedRenderValue? capture = CopyForCacheCapture(value);
                        if (capture is null)
                        {
                            dropped = true;
                            break;
                        }

                        AddCacheCaptureValue(capture);
                        captures.Add(capture);
                    }

                    if (dropped)
                    {
                        // The frame keeps its pixels; only this candidate goes uncached.
                        ReleaseCaptures(captures);

                        _cacheCaptures[decisionIndex] = s_suppressedCacheCapture;
                        continue;
                    }

                    _cacheCaptures[decisionIndex] = captures;
                }
                catch
                {
                    ReleaseCaptures(captures);
                    throw;
                }
            }
        }

        private static long SumDevicePixels(IReadOnlyList<MaterializedRenderValue> values)
        {
            long actualPixels = 0;
            foreach (MaterializedRenderValue value in values)
            {
                long valuePixels = (long)value.DeviceBounds.Width * value.DeviceBounds.Height;
                actualPixels = actualPixels > long.MaxValue - valuePixels
                    ? long.MaxValue
                    : actualPixels + valuePixels;
            }

            return actualPixels;
        }

        // Compared bit for bit: the plan's density is the exact float the value was materialized at.
        private static bool MatchesPlannedDensity(float density, float plannedDensity)
            => BitConverter.SingleToInt32Bits(density) == BitConverter.SingleToInt32Bits(plannedDensity);

        private void ReleaseCaptures(List<MaterializedRenderValue> captures)
        {
            foreach (MaterializedRenderValue capture in captures)
            {
                RemoveCacheCaptureValue(capture);
                ReleaseUnpublished(capture);
            }
        }

        /// <summary>
        /// Copies a value so it can be handed to the render cache, or <see langword="null"/> when a preview
        /// cannot spare the buffer.
        /// </summary>
        /// <remarks>
        /// This copy exists only to warm a cache: the frame is already correct without it. Allocating it the
        /// one way that cannot degrade made it the only thing in a preview that could fail a frame whose
        /// pixels were fine. A delivery session still fails here, because TryAcquire never degrades for one.
        /// </remarks>
        private MaterializedRenderValue? CopyForCacheCapture(MaterializedRenderValue source)
        {
            MaterializedRenderValue capture;
            try
            {
                capture = CreateOwnedValue(
                    source.Bounds,
                    source.EffectiveScale,
                    source.CompleteBounds,
                    source.DeviceBounds,
                    source.DeviceGridOffset,
                    physicalDeviceBoundsAreAligned: true,
                    allowPreviewDrop: true);
            }
            catch (PreviewAllocationDropException)
            {
                return null;
            }

            bool succeeded = false;
            try
            {
                using var canvas = CreateExecutorCanvas(
                    capture.Target,
                    capture.EffectiveScale.Value,
                    _options.MaxWorkingScale,
                    capture.RasterBounds.Size,
                    _options.Intent,
                    capture.DeviceBounds.Position);
                canvas.DrawRenderTargetPixelsWithoutFlush(source.Target, 0, 0);
                succeeded = true;
                return capture;
            }
            finally
            {
                if (!succeeded)
                    ReleaseUnpublished(capture);
            }
        }

        public void RejectCacheCaptures()
        {
            try
            {
                DisposeValues(static (_, isCapture) => isCapture);
            }
            finally
            {
                Array.Clear(_cacheCaptures);
                ClearCacheCaptureValues();
            }
        }

        public bool ValidateCacheCaptures(ref HashSet<RenderNodeCache>? seenCaches)
        {
            if (PreviewAllocationDropObserved)
                return false;
            if (_cacheResolution.MissCaptureCount == 0)
                return false;

            bool found = false;
            for (int decisionIndex = 0; decisionIndex < _cacheResolution.Decisions.Length; decisionIndex++)
            {
                RenderCacheDecision decision = _cacheResolution.Decisions[decisionIndex];
                if (decision.Kind != RenderCacheResolutionKind.MissCapture)
                    continue;
                IReadOnlyList<MaterializedRenderValue> capture = _cacheCaptures[decisionIndex]
                    ?? throw new InvalidOperationException("A selected render-cache miss was not staged.");
                if (ReferenceEquals(capture, s_suppressedCacheCapture))
                    continue;
                RenderNodeCache cache = decision.Candidate.Cache
                    ?? throw new InvalidOperationException("A production cache capture has no node-cache owner.");
                ObjectDisposedException.ThrowIf(cache.IsDisposed, cache);
                if (!(seenCaches ??= new(ReferenceEqualityComparer.Instance)).Add(cache))
                {
                    throw new InvalidOperationException(
                        "One request family cannot atomically publish two independent outputs to the same node cache.");
                }
                found = true;
            }
            return found;
        }

        public void AppendCachePublications(
            ICollection<RenderNodeCachePublication> publications,
            ICollection<RenderTarget> transferredTargets)
        {
            ArgumentNullException.ThrowIfNull(publications);
            ArgumentNullException.ThrowIfNull(transferredTargets);
            if (PreviewAllocationDropObserved)
                return;
            if (_cacheResolution.MissCaptureCount == 0)
                return;

            for (int decisionIndex = 0; decisionIndex < _cacheResolution.Decisions.Length; decisionIndex++)
            {
                RenderCacheDecision decision = _cacheResolution.Decisions[decisionIndex];
                if (decision.Kind != RenderCacheResolutionKind.MissCapture)
                    continue;
                IReadOnlyList<MaterializedRenderValue> values = _cacheCaptures[decisionIndex]
                    ?? throw new InvalidOperationException("A selected render-cache miss was not staged.");
                if (ReferenceEquals(values, s_suppressedCacheCapture))
                    continue;
                RenderNodeCache cache = decision.Candidate.Cache!;
                var cachedValues = new List<RenderNodeCachedValue>(values.Count);
                foreach (MaterializedRenderValue value in values)
                {
                    RenderTarget target = value.TransferToAcceptedCache();
                    transferredTargets.Add(target);
                    cachedValues.Add(new RenderNodeCachedValue(
                        target,
                        value.Bounds,
                        value.EffectiveScale,
                        value.DeviceBounds,
                        value.DeviceGridOffset)
                    {
                        CompleteBounds = value.CompleteBounds,
                    });
                    _ownedValues.Remove(value);
                    RemoveCacheCaptureValue(value);
                }

                publications.Add(new RenderNodeCachePublication(
                    cache,
                    decision.Identity!,
                    cachedValues));
            }
        }

        public void AcceptCacheCaptures()
        {
            if (PreviewAllocationDropObserved)
            {
                RejectCacheCaptures();
                return;
            }

            Array.Clear(_cacheCaptures);
        }
    }
}
