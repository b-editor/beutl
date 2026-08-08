using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class CompatibilityExecutionState
    {
        private void RecordFailure(RenderPipelineFailurePhase phase, long? subjectId)
        {
            FailurePhase ??= phase;
            _diagnostics?.RecordFailure(phase, subjectId);
        }

        private void RecordSynchronization(RenderFragmentReference fragment)
        {
            RenderFragmentId id = fragment.Id
                ?? throw new InvalidOperationException("A synchronizing fragment is not committed.");
            _synchronizations = checked(_synchronizations + 1);
            _diagnostics?.RecordSynchronizationExecuted(id.Value);
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

            var inputs = new List<CompatibilityRenderValue>();
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
                        var token = new RenderExecutionSessionToken();
                        try
                        {
                            token.RunAndComplete(
                                () =>
                                {
                                    IReadOnlyList<RenderExecutionInput> executionInputs = CreateExecutionInputs(
                                        token,
                                        inputs,
                                        requiresReadback: false,
                                        readbackOwner: null,
                                        images);
                                    using (ObserveGpuPass(fragment))
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
                            foreach (SKImage image in images.AsEnumerable().Reverse())
                            {
                                image.Dispose();
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

            Rect outputBounds = run.Output.Bounds;
            Rect requiredRegion = ResolveFragmentRequirement(run.Output, outputBounds);
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

            EffectiveScale inputRequestScale = !run.Output.EffectiveScale.IsUnbounded
                ? run.Output.EffectiveScale
                : EffectiveScale.At(destination.Density);
            IReadOnlyList<CompatibilityRenderValue> inputs = Materialize(
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
                        MarkExecutionSkipped(fragment);
                        return true;
                    }

                    throw new InvalidOperationException(
                        "A directly executed compiled Shader run requires exactly one materialized input.");
                }

                CompatibilityRenderValue input = inputs[0];
                ExecuteReplayIsland(
                    fragment,
                    () => ExecuteCompiledShaderRunProgram(
                        run,
                        input,
                        outputBounds,
                        requiredRegion,
                        outputDeviceBounds,
                        rasterBounds,
                        density,
                        shader =>
                        {
                            using SKShader mapped = shader.WithLocalMatrix(
                                SKMatrix.CreateScaleTranslation(
                                    1f / density,
                                    1f / density,
                                    outputDeviceBounds.X / density,
                                    outputDeviceBounds.Y / density));
                            using var paint = new SKPaint
                            {
                                Shader = mapped,
                                IsAntialias = false,
                            };
                            destination.VerifyAccess();
                            destination.Canvas.DrawRect(rasterBounds.ToSKRect(), paint);
                        }));
                return true;
            }
            finally
            {
                CompleteFragmentUse(run.Input);
            }
        }

        private void DrawMaterializedFragment(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            IReadOnlyList<CompatibilityRenderValue> values = Materialize(
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
            if (_previewAllocationDropObserved)
                return;
            if (_pendingCacheCaptures.Count + _suppressedCacheCaptures.Count
                != _cacheResolution.MissCaptures.Length)
            {
                throw new InvalidOperationException(
                    "Every selected render-cache miss must materialize exactly one staged capture.");
            }

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
            if (_previewAllocationDropObserved)
                return;
            var byCandidate = _pendingCacheCaptures.ToDictionary(static item => item.Descriptor.CandidateId);
            foreach (RenderCacheMissCapture descriptor in _cacheResolution.MissCaptures)
            {
                if (_suppressedCacheCaptures.Contains(descriptor.CandidateId))
                    continue;
                PendingRenderCacheCapture pending = byCandidate[descriptor.CandidateId];
                RenderNodeCache cache = _cacheResolution.GetDecision(descriptor.CandidateId).Candidate.Cache!;
                var cachedValues = new List<RenderNodeCachedValue>(pending.Values.Count);
                foreach (CompatibilityRenderValue value in pending.Values)
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
                    if (_diagnosticIntermediates.Remove(value))
                        _diagnostics?.RecordIntermediateDischarged();
                }

                publications.Add(new RenderNodeCachePublication(
                    cache,
                    descriptor.Identity,
                    cachedValues));
            }
        }

        public void AcceptCacheCaptures()
        {
            if (_previewAllocationDropObserved)
            {
                RejectCacheCaptures();
                return;
            }

            _pendingCacheCaptures.Clear();
            _suppressedCacheCaptures.Clear();
            _diagnostics?.CommitAcceptedCacheCaptures();
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

        private void DisposeValues(Func<CompatibilityRenderValue, bool, bool> predicate)
        {
            List<Exception>? failures = null;
            foreach (CompatibilityRenderValue value in _ownedValues.Reverse().ToArray())
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
                _intermediateTargetAcquisitions,
                _programCacheHits,
                _synchronizations);

        public void ValidateExecutionCompleted(bool allowSkippedIslands)
            => _executionLedger.ValidateCompleted(
                allowSkippedIslands || _previewAllocationDropObserved || _verificationExecutionAbandoned,
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
