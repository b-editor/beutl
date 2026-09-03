using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class RenderRequestExecutionState
    {
        private void RecordSynchronization()
        {
            _synchronizations = checked(_synchronizations + 1);
        }

        private bool TryReplayEngineSourceDirect(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            OpaqueRenderDescription description =
                ((OpaqueRenderFragmentPayload)fragment.Payload!).Description;
            if (description.DirectReplay is not { } replay
                || !fragment.ContributesValuesToTarget
                || _values.ContainsKey(fragment)
                || fragment.Id is { } id
                    && (_cacheHits.ContainsKey(id) || _cacheMisses.ContainsKey(id))
                || _resourceUses.GetRemainingUseCount(fragment) != 1)
            {
                return false;
            }

            bool replayAtExactReduction = description.DirectReplayAtExactIntegerReduction
                && RenderScaleUtilities.IsExactIntegerReduction(destination.Density);
            float replayScale = replayAtExactReduction
                ? destination.Density
                : fragment.EffectiveScale.Value;
            if (!fragment.EffectiveScale.IsUnbounded
                && (!replayAtExactReduction && fragment.EffectiveScale.Value != destination.Density
                    || !DirectRenderTargetGeometry.FromCanvas(destination).CanDrawPixelAligned(
                        fragment.Bounds,
                        replayScale,
                        PixelRect.FromRect(fragment.Bounds, replayScale).Size)))
            {
                return false;
            }

            var inputs = new List<MaterializedRenderValue>();
            EffectiveScale outputSupply = fragment.EffectiveScale.IsUnbounded
                ? EffectiveScale.At(destination.Density)
                : fragment.EffectiveScale;
            try
            {
                foreach (RenderFragmentReference input in fragment.Inputs)
                {
                    inputs.AddRange(Materialize(
                        input,
                        destination,
                        input.EffectiveScale.IsUnbounded ? outputSupply : null));
                }

                ExecuteReplayIsland(
                    fragment,
                    () =>
                    {
                        var images = new List<SKImage>();
                        RenderExecutionSessionToken token = CreateExecutionSessionToken();
                        try
                        {
                            token.RunAndComplete(
                                () =>
                                {
                                    IReadOnlyList<RenderExecutionInput> executionInputs = CreateExecutionInputs(
                                        token,
                                        inputs,
                                        requiresReadback: false,
                                        images);
                                    using (destination.BeginDirectExecution(token))
                                    {
                                        replay(new EngineDirectRenderSession(
                                            token,
                                            destination,
                                            executionInputs));
                                    }
                                });
                        }
                        finally
                        {
                            // Reverse index walk: the LINQ form buffers the whole list before yielding,
                            // and this runs in a per-frame teardown path.
                            for (int index = images.Count - 1; index >= 0; index--)
                            {
                                images[index].Dispose();
                            }
                        }
                    });
                return true;
            }
            finally
            {
                foreach (RenderFragmentReference input in fragment.Inputs)
                    CompleteFragmentUse(input);
            }
        }

        private bool TryExecuteCompiledShaderRunDirect(
            RenderFragmentReference fragment,
            CompiledShaderRun run,
            ImmediateCanvas destination)
        {
            // The Vulkan-native path consumes and produces pooled RGBA16F textures. Keep it behind the ordinary
            // materialization boundary instead of recording GPU work directly into a Skia replay destination.
            if (ShouldDeferDirectReplayToSpirv(run))
                return false;

            if (!ReferenceEquals(run.Output, fragment)
                || !_roots.Contains(fragment)
                || !fragment.ContributesValuesToTarget
                || _values.ContainsKey(fragment)
                || fragment.Id is { } id
                    && (_cacheHits.ContainsKey(id) || _cacheMisses.ContainsKey(id))
                || _resourceUses.GetRemainingUseCount(fragment) != 1)
            {
                return false;
            }

            if (!DirectShaderRunPlanner.TryResolve(
                    fragment,
                    run,
                    _regions,
                    DirectRenderTargetGeometry.FromCanvas(destination),
                    out DirectShaderRunPlan directPlan))
            {
                return false;
            }

            EffectiveScale inputRequestScale = !run.Output.EffectiveScale.IsUnbounded
                ? run.Output.EffectiveScale
                : EffectiveScale.At(destination.Density);
            IReadOnlyList<MaterializedRenderValue> inputs = Materialize(
                run.Input,
                destination,
                run.Input.EffectiveScale.IsUnbounded ? inputRequestScale : null);
            try
            {
                if (inputs.Count != 1)
                {
                    if (inputs.Count == 0)
                    {
                        ExecutionIsland island = _executionLedger.Begin(fragment);
                        _executionLedger.Complete(island);
                        return true;
                    }

                    throw new InvalidOperationException(
                        "A directly executed compiled Shader run requires exactly one materialized input.");
                }

                MaterializedRenderValue input = inputs[0];
                ExecuteReplayIsland(
                    fragment,
                    () => ExecuteCompiledShaderRunProgram(
                        run,
                        input,
                        directPlan.OutputBounds,
                        directPlan.RequiredRegion,
                        directPlan.OutputDeviceBounds,
                        directPlan.RasterBounds,
                        directPlan.Density,
                        shader =>
                        {
                            using SKShader mapped = shader.WithLocalMatrix(
                                SKMatrix.CreateScaleTranslation(
                                    1f / directPlan.Density,
                                    1f / directPlan.Density,
                                    directPlan.OutputDeviceBounds.X / directPlan.Density,
                                    directPlan.OutputDeviceBounds.Y / directPlan.Density));
                            using var paint = new SKPaint
                            {
                                Shader = mapped,
                                IsAntialias = false,
                            };
                            destination.VerifyAccess();
                            destination.Canvas.DrawRect(directPlan.RasterBounds.ToSKRect(), paint);
                        }));
                return true;
            }
            finally
            {
                CompleteFragmentUse(run.Input);
            }
        }

        private bool TryReplayBuiltInSkiaFilterChainDirect(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            var chain = new List<(
                RenderFragmentReference Fragment,
                FilterEffectSegmentRenderFragmentPayload Payload)>();
            RenderFragmentReference input = fragment;
            while (TryGetDirectSkiaFilterSegment(input, destination, out var payload))
            {
                chain.Add((input, payload));
                input = input.Inputs[0];
            }

            if (chain.Count == 0)
                return false;

            // Every link fuses into the one save layer, so only the fragment the walk stopped at can
            // reach it as a buffer, and a buffer survives the copy only where the destination transform
            // lands it on whole device pixels. An unbounded input is re-rasterized inside the layer.
            if (!input.EffectiveScale.IsUnbounded
                && (input.EffectiveScale.Value != destination.Density
                    || !CanCopyPixelsToDestination(chain[^1].Fragment.Bounds, destination)))
            {
                return false;
            }

            IReadOnlyList<MaterializedRenderValue>? materializedInput = null;
            if (input.ValueCardinality.Maximum is > 1 or null)
            {
                if (!input.ContributesValuesToTarget
                    || !CanCopyPixelsToDestination(fragment.Bounds, destination))
                {
                    return false;
                }

                materializedInput = Materialize(
                    input,
                    destination,
                    input.EffectiveScale.IsUnbounded
                        ? EffectiveScale.At(destination.Density)
                        : null);
                if (materializedInput.Count > 1)
                    return false;
            }

            using var builder = new SKImageFilterBuilder();
            for (int segmentIndex = chain.Count - 1; segmentIndex >= 0; segmentIndex--)
            {
                foreach (IFEItem item in chain[segmentIndex].Payload.BoundsItems)
                    ((IFEItem_Skia)item).AcceptsDirect(builder);
            }

            using var paint = builder.HasFilter()
                ? new SKPaint { ImageFilter = builder.GetFilter() }
                : null;
            Rect replayedInputBounds = ResolveFragmentRequirement(input, input.Bounds);
            Rect layerContentBounds = GetDirectFilterLayerBounds(
                input.Bounds,
                replayedInputBounds,
                materializedInput is { Count: 1 } ? materializedInput[0].RasterBounds : null);
            ExecuteSegment(chainIndex: 0);
            return true;

            void ExecuteSegment(int chainIndex)
            {
                (RenderFragmentReference current, _) = chain[chainIndex];
                ExecuteReplayIsland(
                    current,
                    () =>
                    {
                        int nextIndex = chainIndex + 1;
                        if (nextIndex < chain.Count)
                        {
                            ExecuteSegment(nextIndex);
                            CompleteFragmentUse(chain[nextIndex].Fragment);
                        }
                        else if (paint is not null)
                        {
                            using (destination.PushBlendMode(BlendMode.SrcOver))
                            using (destination.PushTransform(Matrix.Identity))
                            // Bound the layer to exactly what ReplayInput draws; filters must not sample
                            // unwritten portions of the input's semantic bounds as source pixels.
                            using (destination.PushFilterLayer(paint, layerContentBounds))
                            {
                                ReplayInput();
                            }
                        }
                        else
                        {
                            ReplayInput();
                        }
                    });
            }

            void ReplayInput()
            {
                if (materializedInput is null)
                {
                    Replay(input, destination);
                    return;
                }

                if (materializedInput.Count == 1)
                    DrawValues(materializedInput, destination);
                CompleteFragmentUse(input);
            }
        }

        private bool TryGetDirectSkiaFilterSegment(
            RenderFragmentReference fragment,
            ImmediateCanvas destination,
            out FilterEffectSegmentRenderFragmentPayload payload)
        {
            payload = null!;
            if (!fragment.ContributesValuesToTarget
                || fragment.Inputs.Length != 1
                || _values.ContainsKey(fragment)
                || fragment.Id is { } id
                    && (_cacheHits.ContainsKey(id) || _cacheMisses.ContainsKey(id))
                || _resourceUses.GetRemainingUseCount(fragment) != 1
                || !fragment.EffectiveScale.IsUnbounded
                    && fragment.EffectiveScale.Value != destination.Density
                || fragment.Payload is not FilterEffectSegmentRenderFragmentPayload directPayload
                || !directPayload.SupportsDirectReplay)
            {
                return false;
            }

            payload = directPayload;
            return true;
        }

        /// <summary>
        /// Reports whether a buffer covering <paramref name="bounds"/> lands on whole device pixels of
        /// <paramref name="destination"/>, so copying it costs nothing.
        /// </summary>
        private static bool CanCopyPixelsToDestination(Rect bounds, ImmediateCanvas destination)
            => DirectRenderTargetGeometry.FromCanvas(destination).CanDrawPixelAligned(
                bounds,
                destination.Density,
                PixelRect.FromRect(bounds, destination.Density).Size);

        private void DrawMaterializedFragment(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            IReadOnlyList<MaterializedRenderValue> values = Materialize(
                fragment,
                destination,
                fragment.EffectiveScale.IsUnbounded
                    ? EffectiveScale.At(destination.Density)
                    : null);
            if (fragment.ContributesValuesToTarget)
                DrawValues(values, destination);
        }

        private void ExecuteReplayIsland(RenderFragmentReference fragment, Action execute)
        {
            ExecutionIsland island = _executionLedger.Begin(fragment);
            execute();
            _executionLedger.Complete(island);
        }

        public void DisposeNonCacheValues()
        {
            try
            {
                DisposeValues(static (_, isCapture) => !isCapture);
            }
            finally
            {
                _values.Clear();
                _valueReferences.Clear();
                _backdropCaptures.Clear();
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
                _pendingCacheCaptures.Clear();
                _suppressedCacheCaptures.Clear();
                _cacheCaptureValues.Clear();
            }
        }

        public void ValidateCacheCaptures(ISet<RenderNodeCache> seenCaches)
        {
            ArgumentNullException.ThrowIfNull(seenCaches);
            if (PreviewAllocationDropObserved)
                return;
            if (_pendingCacheCaptures.Count + _suppressedCacheCaptures.Count
                != _cacheResolution.MissCaptures.Length)
            {
                throw new InvalidOperationException(
                    "Every selected render-cache miss must materialize exactly one staged capture.");
            }

            // Nothing to index when nothing was selected for capture, which is every frame the resolver
            // took no cache decision - and this runs on every published frame.
            if (_cacheResolution.MissCaptures.IsEmpty)
                return;

            var byCandidate = _pendingCacheCaptures.ToDictionary(static item => item.Descriptor.CandidateId);
            foreach (RenderCacheMissCapture descriptor in _cacheResolution.MissCaptures)
            {
                if (_suppressedCacheCaptures.Contains(descriptor.CandidateId))
                    continue;
                if (!byCandidate.ContainsKey(descriptor.CandidateId))
                    throw new InvalidOperationException("A selected render-cache miss was not staged.");
                RenderNodeCache cache = _cacheResolution.GetDecision(descriptor.CandidateId).Candidate.Cache
                    ?? throw new InvalidOperationException("A production cache capture has no node-cache owner.");
                ObjectDisposedException.ThrowIf(cache.IsDisposed, cache);
                if (!seenCaches.Add(cache))
                {
                    throw new InvalidOperationException(
                        "One request family cannot atomically publish two independent outputs to the same node cache.");
                }
            }
        }

        public void AppendCachePublications(
            ICollection<RenderNodeCachePublication> publications,
            ICollection<RenderTarget> transferredTargets)
        {
            ArgumentNullException.ThrowIfNull(publications);
            ArgumentNullException.ThrowIfNull(transferredTargets);
            if (PreviewAllocationDropObserved)
                return;
            // Nothing to index when nothing was selected for capture, which is every frame the resolver
            // took no cache decision - and this runs on every published frame.
            if (_cacheResolution.MissCaptures.IsEmpty)
                return;

            var byCandidate = _pendingCacheCaptures.ToDictionary(static item => item.Descriptor.CandidateId);
            foreach (RenderCacheMissCapture descriptor in _cacheResolution.MissCaptures)
            {
                if (_suppressedCacheCaptures.Contains(descriptor.CandidateId))
                    continue;
                PendingRenderCacheCapture pending = byCandidate[descriptor.CandidateId];
                RenderNodeCache cache = _cacheResolution.GetDecision(descriptor.CandidateId).Candidate.Cache!;
                var cachedValues = new List<RenderNodeCachedValue>(pending.Values.Count);
                foreach (MaterializedRenderValue value in pending.Values)
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
                    _cacheCaptureValues.Remove(value);
                }

                publications.Add(new RenderNodeCachePublication(
                    cache,
                    descriptor.Identity,
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

            _pendingCacheCaptures.Clear();
            _suppressedCacheCaptures.Clear();
        }

        public void Dispose()
        {
            var failures = new List<Exception>();
            try
            {
                DisposeValues(static (_, _) => true);
            }
            catch (AggregateException aggregate)
            {
                failures.AddRange(aggregate.Flatten().InnerExceptions);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
            finally
            {
                _pendingCacheCaptures.Clear();
                _suppressedCacheCaptures.Clear();
                _cacheCaptureValues.Clear();
            }

            try
            {
                RejectBuiltInBackdropCaptures();
            }
            catch (AggregateException aggregate)
            {
                failures.AddRange(aggregate.Flatten().InnerExceptions);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }

            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            if (failures.Count > 1)
                throw new AggregateException("One or more execution-state resources failed to dispose.", failures);
        }

        private void DisposeValues(Func<MaterializedRenderValue, bool, bool> predicate)
        {
            List<Exception>? failures = null;
            // The loop removes from _ownedValues, so it needs a snapshot; Reverse() already buffers the set
            // into an array that ToArray() then copies a second time, and a set has no order to preserve.
            var snapshot = new MaterializedRenderValue[_ownedValues.Count];
            _ownedValues.CopyTo(snapshot);
            foreach (MaterializedRenderValue value in snapshot)
            {
                bool isCapture = _cacheCaptureValues.Contains(value);
                if (!predicate(value, isCapture))
                    continue;

                _ownedValues.Remove(value);
                _cacheCaptureValues.Remove(value);
                try
                {
                    DisposeOwnedValue(value);
                }
                catch (Exception ex)
                {
                    (failures ??= []).Add(ex);
                }
            }

            if (failures is null)
                return;
            if (failures.Count == 1)
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            throw new AggregateException("One or more render values failed to dispose.", failures);
        }

        public RenderExecutionStatistics CreateStatistics()
            => new(
                _shaderRunExecutions,
                _shaderStageExecutions,
                _fusedShaderRunExecutions,
                _spirvShaderRunExecutions,
                _intermediateTargetAcquisitions,
                _programCacheHits,
                _synchronizations);

        public void ValidateExecutionCompleted(bool allowSkippedIslands)
            => _executionLedger.ValidateCompleted(
                allowSkippedIslands || PreviewAllocationDropObserved,
                _regionEmptyIslands);

        private static bool IsRegionEmpty(ExecutionIsland island, RegionAnalysis regions)
        {
            foreach (RenderFragmentId fragmentId in island.Fragments)
            {
                if (!regions.FragmentRequirements.TryGetValue(fragmentId, out RequiredRegion requirement)
                    || !requirement.IsEmpty)
                {
                    return false;
                }

                if (regions.TargetAccessRequirements.TryGetValue(
                        fragmentId,
                        out RequiredRegion targetRequirement)
                    && !targetRequirement.IsEmpty)
                {
                    return false;
                }
            }

            return true;
        }

    }
}

internal readonly record struct DirectRenderTargetGeometry(float Density, Matrix Transform)
{
    public static DirectRenderTargetGeometry FromCanvas(ImmediateCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        return new DirectRenderTargetGeometry(canvas.Density, canvas.Transform);
    }

    public bool CanDrawPixelAligned(Rect destination, float sourceDensity, PixelSize sourceSize)
        => ImmediateCanvas.CanDrawPixelAligned(
            destination,
            sourceDensity,
            sourceSize,
            Density,
            Transform);
}

internal readonly record struct DirectShaderRunPlan(
    Rect OutputBounds,
    Rect RequiredRegion,
    PixelRect OutputDeviceBounds,
    Rect RasterBounds,
    float Density);

internal static class DirectShaderRunPlanner
{
    public static bool TryResolve(
        RenderFragmentReference fragment,
        CompiledShaderRun run,
        RegionAnalysis regions,
        DirectRenderTargetGeometry destination,
        out DirectShaderRunPlan plan)
    {
        plan = default;
        if (!ReferenceEquals(run.Output, fragment))
            return false;

        Rect outputBounds = run.Output.Bounds;
        RenderFragmentReference requirementFragment = run.WholeSourceHead is null
            ? run.Output
            : run.Stages[0].Fragment;
        Rect requiredRegion = regions.GetFragmentRequirement(requirementFragment).Resolve(outputBounds);
        if (requiredRegion.Width == 0 || requiredRegion.Height == 0)
            return false;

        float requestedDensity = fragment.EffectiveScale.IsUnbounded
            ? destination.Density
            : fragment.EffectiveScale.Value;
        float density = RenderScaleUtilities.ClampWorkingScaleToBufferBudget(
            outputBounds,
            requestedDensity);
        if (density != destination.Density)
            return false;

        PixelRect outputDeviceBounds = PixelRect.FromRect(requiredRegion, density);
        Rect rasterBounds = outputDeviceBounds.ToRect(density);
        if (!destination.CanDrawPixelAligned(
                rasterBounds,
                density,
                outputDeviceBounds.Size))
        {
            return false;
        }

        plan = new DirectShaderRunPlan(
            outputBounds,
            requiredRegion,
            outputDeviceBounds,
            rasterBounds,
            density);
        return true;
    }
}
