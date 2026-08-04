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
        private IReadOnlyList<CompatibilityRenderValue> Materialize(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale = null)
        {
            if (fragment.EffectiveScale.IsUnbounded)
            {
                if (!_materializationDemands.TryGetValue(fragment, out EffectiveScale demand))
                {
                    throw new InvalidOperationException(
                        "An executable fragment is not reachable from the request publication roots.");
                }

                if (requestedScale is { } callerRequest)
                {
                    float callerDensity = MathF.Min(
                        callerRequest.Value,
                        RenderScaleUtilities.SanitizeMaxWorkingScale(_options.MaxWorkingScale));
                    callerDensity = RenderMaterializationDensityPolicy.Clamp(
                        fragment,
                        callerDensity);
                    if (callerDensity > demand.Value)
                    {
                        throw new InvalidOperationException(
                            "The compiled materialization demand does not cover its contextual caller.");
                    }
                }

                requestedScale = demand;
            }

            long? previous = ActiveSubjectId;
            ActiveSubjectId = fragment.Id?.Value;
            try
            {
                IReadOnlyList<CompatibilityRenderValue> result = MaterializeCore(
                    fragment,
                    currentTarget,
                    requestedScale);
                if (fragment.Id is { } id
                    && !_cacheHits.ContainsKey(id)
                    && !_skippedExecutionSubjects.Contains(id))
                {
                    _diagnostics?.RecordFragmentExecuted(id.Value);
                }
                return result;
            }
            catch (PreviewAllocationDropException)
            {
                throw;
            }
            catch
            {
                RecordFailure(RenderPipelineFailurePhase.Execution, fragment.Id?.Value);
                throw;
            }
            finally
            {
                ActiveSubjectId = previous;
            }
        }

        private IReadOnlyList<CompatibilityRenderValue> MaterializeCore(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale = null)
        {
            if (_values.TryGetValue(fragment, out IReadOnlyList<CompatibilityRenderValue>? cached))
                return cached;

            IReadOnlyList<CompatibilityRenderValue> result;
            bool cacheHit = TryMaterializeCacheHit(
                fragment,
                out IReadOnlyList<CompatibilityRenderValue>? hitValues);
            if (cacheHit)
            {
                result = hitValues!;
            }
            else if (_executionPlan.TryGetMembership(fragment, out ExecutionIslandMembership membership))
            {
                ExecutionIsland island = _executionLedger.Begin(fragment);
                result = membership.ShaderRun is { } run
                    ? ExecuteCompiledShaderRun(run, currentTarget, requestedScale)
                    : MaterializePlannedFragment(fragment, currentTarget, requestedScale);
                _executionLedger.Complete(island);
            }
            else
            {
                result = fragment.Kind switch
                {
                    RenderFragmentKind.MaterializedInput => MaterializeExternal(fragment),
                    RenderFragmentKind.ContributeValues => MaterializeSingleInput(fragment, currentTarget),
                    _ => throw new InvalidOperationException(
                        $"Executable fragment '{fragment.Kind}' is not assigned to an execution island."),
                };
            }
            StageCacheCaptures(fragment, result);
            _values.Add(fragment, result);
            AddValueReferences(result);
            if (fragment.Kind == RenderFragmentKind.ContributeValues && !cacheHit)
                CompleteFragmentUse(fragment.Inputs.Single());
            return result;
        }

        private IReadOnlyList<CompatibilityRenderValue> MaterializePlannedFragment(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
            => fragment.Kind switch
            {
                RenderFragmentKind.OpaqueSource
                    or RenderFragmentKind.OpaqueMap
                    or RenderFragmentKind.OpaqueCombine
                    or RenderFragmentKind.OpaqueExpand => ExecuteOpaque(fragment, currentTarget, requestedScale),
                RenderFragmentKind.LegacyFilterEffect => ExecuteLegacyFilter(fragment, currentTarget),
                RenderFragmentKind.Shader => ExecuteShader(fragment, currentTarget, requestedScale),
                RenderFragmentKind.Geometry => ExecuteGeometry(fragment, currentTarget),
                RenderFragmentKind.Opacity => MaterializeOpacity(fragment, currentTarget, requestedScale),
                RenderFragmentKind.OpacityMask => MaterializeOpacityMask(fragment, currentTarget, requestedScale),
                RenderFragmentKind.Layer => MaterializeLayer(fragment, requestedScale),
                RenderFragmentKind.TargetCapture
                    or RenderFragmentKind.BuiltInBackdropCapture => CaptureTarget(fragment, currentTarget),
                RenderFragmentKind.TargetScope
                    when ((TargetScopeRenderFragmentPayload)fragment.Payload!).Description.IsValueReplayMap
                    => MaterializeValueReplayMap(fragment, currentTarget, requestedScale),
                _ => throw new NotSupportedException(
                    $"The planned fragment '{fragment.Kind}' cannot be materialized as a value."),
            };

        private IReadOnlyList<CompatibilityRenderValue> MaterializeSingleInput(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale = null)
        {
            if (fragment.Inputs.Length != 1)
                throw new InvalidOperationException("A unary recorded fragment requires exactly one input.");
            return Materialize(fragment.Inputs[0], currentTarget, requestedScale);
        }

        private bool TryMaterializeCacheHit(
            RenderFragmentReference fragment,
            out IReadOnlyList<CompatibilityRenderValue>? values)
        {
            if (fragment.Id is not { } id || !_cacheHits.TryGetValue(id, out RenderCacheHitSubstitution? hit))
            {
                values = null;
                return false;
            }

            if (hit.Entry.Payload is not RenderNodeCachedOutput cachedOutput)
            {
                throw new InvalidOperationException(
                    "A selected render-cache hit does not contain a node-cache output payload.");
            }

            var acquired = new List<CompatibilityRenderValue>(cachedOutput.Values.Count);
            bool supportsIndependentOutputDensities = fragment.SupportsIndependentOutputDensities;
            try
            {
                foreach (RenderNodeCachedValue cached in cachedOutput.Values)
                {
                    if (cached.EffectiveScale.IsUnbounded
                        || (!supportsIndependentOutputDensities
                            && BitConverter.SingleToInt32Bits(cached.EffectiveScale.Value)
                            != BitConverter.SingleToInt32Bits(hit.Identity.Density)))
                    {
                        throw new InvalidOperationException(
                            "A render-cache hit payload does not match its planned materialization density.");
                    }

                    CompatibilityRenderValue value = CreateOwnedShallowCopy(
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
                foreach (CompatibilityRenderValue value in acquired)
                    ReleaseUnpublished(value);
                throw;
            }

            _diagnostics?.RecordOutcome(id.Value, RenderPipelineOutcome.Cached);
            values = acquired;
            return true;
        }

        private void StageCacheCaptures(
            RenderFragmentReference fragment,
            IReadOnlyList<CompatibilityRenderValue> values)
        {
            if (_previewAllocationDropObserved)
                return;
            if (fragment.Id is not { } id || !_cacheMisses.TryGetValue(id, out var misses))
                return;

            bool supportsIndependentOutputDensities = fragment.SupportsIndependentOutputDensities;
            long actualPixels = 0;
            foreach (CompatibilityRenderValue value in values)
            {
                long valuePixels = (long)value.DeviceBounds.Width * value.DeviceBounds.Height;
                actualPixels = actualPixels > long.MaxValue - valuePixels
                    ? long.MaxValue
                    : actualPixels + valuePixels;
            }

            foreach (RenderCacheMissCapture miss in misses)
            {
                if (!_options.CachePolicy.Rules.Match(actualPixels))
                {
                    _suppressedCacheCaptures.Add(miss.CandidateId);
                    _diagnostics?.RecordCacheCaptureRejected();
                    continue;
                }

                var captures = new List<CompatibilityRenderValue>(values.Count);
                try
                {
                    foreach (CompatibilityRenderValue value in values)
                    {
                        if (!supportsIndependentOutputDensities
                            && BitConverter.SingleToInt32Bits(value.EffectiveScale.Value)
                            != BitConverter.SingleToInt32Bits(miss.Identity.Density))
                        {
                            throw new InvalidOperationException(
                                "A render-cache capture does not match its planned materialization density.");
                        }

                        CompatibilityRenderValue capture = CopyForCacheCapture(value);
                        _cacheCaptureValues.Add(capture);
                        captures.Add(capture);
                    }

                    _pendingCacheCaptures.Add(new PendingRenderCacheCapture(miss, captures));
                    _diagnostics?.RecordCacheCaptureStaged(miss.ProducerId.Value);
                }
                catch
                {
                    foreach (CompatibilityRenderValue capture in captures)
                    {
                        _cacheCaptureValues.Remove(capture);
                        ReleaseUnpublished(capture);
                    }
                    throw;
                }
            }
        }

        private CompatibilityRenderValue CopyForCacheCapture(CompatibilityRenderValue source)
        {
            CompatibilityRenderValue capture = CreateOwnedValue(
                source.Bounds,
                source.EffectiveScale,
                source.CompleteBounds,
                source.DeviceBounds,
                source.DeviceGridOffset,
                physicalDeviceBoundsAreAligned: true);
            bool succeeded = false;
            try
            {
                using var canvas = ImmediateCanvas.CreateExecutorManaged(
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

    }
}
