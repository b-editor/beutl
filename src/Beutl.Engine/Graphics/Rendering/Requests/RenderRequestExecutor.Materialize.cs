using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class RenderRequestExecutionState
    {
        private IReadOnlyList<MaterializedRenderValue> Materialize(
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

            return MaterializeCore(
                fragment,
                currentTarget,
                requestedScale);
        }

        private IReadOnlyList<MaterializedRenderValue> MaterializeCore(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale = null)
        {
            if (_values.TryGetValue(fragment, out IReadOnlyList<MaterializedRenderValue>? cached))
                return cached;

            IReadOnlyList<MaterializedRenderValue> result;
            bool cacheHit = TryMaterializeCacheHit(
                fragment,
                out IReadOnlyList<MaterializedRenderValue>? hitValues);
            if (cacheHit)
            {
                result = hitValues!;
            }
            else
            {
                result = ExecuteFragment(fragment, currentTarget, requestedScale);
            }
            StageCacheCaptures(fragment, result);
            _values.Add(fragment, result);
            AddValueReferences(result);
            if (fragment.Kind == RenderFragmentKind.ContributeValues && !cacheHit)
                CompleteFragmentUse(fragment.Inputs.Single());
            return result;
        }

        private IReadOnlyList<MaterializedRenderValue> ExecuteFragment(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            if (_executionPlan.TryGetMembership(_graph, fragment, out ExecutionIslandMembership membership))
            {
                ExecutionIsland island = _executionLedger.Begin(membership);
                IReadOnlyList<MaterializedRenderValue> values = membership.Island.ShaderRun is { } run
                    ? ExecuteCompiledShaderRun(run, currentTarget, requestedScale)
                    : MaterializePlannedFragment(fragment, currentTarget, requestedScale);
                _executionLedger.Complete(island);
                return values;
            }

            return fragment.Kind switch
            {
                RenderFragmentKind.MaterializedInput => MaterializeExternal(fragment),
                RenderFragmentKind.ContributeValues => MaterializeSingleInput(fragment, currentTarget),
                _ => throw new InvalidOperationException(
                    $"Executable fragment '{fragment.Kind}' is not assigned to an execution island."),
            };
        }

        private IReadOnlyList<MaterializedRenderValue> MaterializePlannedFragment(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
            => fragment.Kind switch
            {
                RenderFragmentKind.OpaqueSource
                    or RenderFragmentKind.OpaqueMap
                    or RenderFragmentKind.OpaqueCombine
                    or RenderFragmentKind.OpaqueExpand => ExecuteOpaque(fragment, currentTarget, requestedScale),
                RenderFragmentKind.FilterEffectSegment => ExecuteEffectItem(fragment, currentTarget),
                RenderFragmentKind.Shader => ExecuteShader(fragment, currentTarget, requestedScale),
                RenderFragmentKind.Geometry => ExecuteGeometry(fragment, currentTarget),
                RenderFragmentKind.Opacity => MaterializeOpacity(fragment, currentTarget, requestedScale),
                RenderFragmentKind.OpacityMask => MaterializeOpacityMask(fragment, currentTarget, requestedScale),
                RenderFragmentKind.Layer => MaterializeLayer(fragment, currentTarget, requestedScale),
                RenderFragmentKind.TargetCapture
                    or RenderFragmentKind.BuiltInBackdropCapture => CaptureTarget(fragment, currentTarget),
                RenderFragmentKind.TargetScope
                    when ((TargetScopeRenderFragmentPayload)fragment.Payload!).Description.IsValueReplayMap
                    => MaterializeValueReplayMap(fragment, currentTarget, requestedScale),
                _ => throw new NotSupportedException(
                    $"The planned fragment '{fragment.Kind}' cannot be materialized as a value."),
            };

        private IReadOnlyList<MaterializedRenderValue> MaterializeSingleInput(
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
                            && BitConverter.SingleToInt32Bits(cached.EffectiveScale.Value)
                            != BitConverter.SingleToInt32Bits(hit.Identity!.Density)))
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
            long actualPixels = 0;
            foreach (MaterializedRenderValue value in values)
            {
                long valuePixels = (long)value.DeviceBounds.Width * value.DeviceBounds.Height;
                actualPixels = actualPixels > long.MaxValue - valuePixels
                    ? long.MaxValue
                    : actualPixels + valuePixels;
            }

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
                            && BitConverter.SingleToInt32Bits(value.EffectiveScale.Value)
                            != BitConverter.SingleToInt32Bits(miss.Identity!.Density))
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

                        _cacheCaptureValues.Add(capture);
                        captures.Add(capture);
                    }

                    if (dropped)
                    {
                        // The frame keeps its pixels; only this candidate goes uncached.
                        foreach (MaterializedRenderValue partial in captures)
                        {
                            _cacheCaptureValues.Remove(partial);
                            ReleaseUnpublished(partial);
                        }

                        _cacheCaptures[decisionIndex] = s_suppressedCacheCapture;
                        continue;
                    }

                    _cacheCaptures[decisionIndex] = captures;
                }
                catch
                {
                    foreach (MaterializedRenderValue capture in captures)
                    {
                        _cacheCaptureValues.Remove(capture);
                        ReleaseUnpublished(capture);
                    }
                    throw;
                }
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

    }
}
