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
        private void ReplayOpacityMask(
            RenderFragmentReference fragment,
            ImmediateCanvas destination,
            EffectiveScale callerScale)
        {
            if (fragment.Inputs.Length != 1)
                throw new InvalidOperationException("An opacity mask requires exactly one input.");

            var payload = (OpacityMaskRenderFragmentPayload)fragment.Payload!;
            _ = payload.Mask.Registry.Use(
                payload.Mask,
                mask =>
                {
                    using (destination.PushOpacityMask(mask, payload.BrushBounds, payload.Invert))
                        Replay(fragment.Inputs[0], destination, callerScale);
                    return true;
                });
        }

        private IReadOnlyList<MaterializedRenderValue> MaterializeOpacityComposite(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            if (fragment.Inputs.Length != 1)
            {
                throw new InvalidOperationException(fragment.Kind == RenderFragmentKind.OpacityMask
                    ? "An opacity mask requires exactly one input."
                    : "An opacity fragment requires exactly one input.");
            }
            if (fragment.Bounds.Width == 0 || fragment.Bounds.Height == 0)
            {
                CompleteFragmentUse(fragment.Inputs[0]);
                return [];
            }

            EffectiveScale scale = ClampToActiveDeviceGrid(
                fragment.Bounds,
                requestedScale ?? ResolveConcreteScale(fragment));
            RenderFragmentReference input = fragment.Inputs[0];
            IReadOnlyList<MaterializedRenderValue> values = Materialize(
                input,
                currentTarget,
                input.EffectiveScale.IsUnbounded ? scale : null);
            try
            {
                MaterializedRenderValue value = CreateOwnedValue(
                    fragment.Bounds,
                    scale,
                    allowPreviewDrop: _previewDropEligibleMaterializations.Contains(fragment));
                value.TextPart = TextPartTag.GetCommon(values, static value => value.TextPart);
                bool succeeded = false;
                try
                {
                    using var canvas = CreateValueCanvas(value);
                    using (canvas.PushTransform(value.RasterAlignmentTransform))
                    {
                        if (fragment.Payload is OpacityMaskRenderFragmentPayload mask)
                        {
                            DrawMasked(mask, values, canvas);
                        }
                        else
                        {
                            using (canvas.PushOpacity(((OpacityRenderFragmentPayload)fragment.Payload!).Opacity))
                                DrawValues(values, canvas);
                        }
                    }

                    succeeded = true;
                    return [value];
                }
                finally
                {
                    if (!succeeded)
                        ReleaseUnpublished(value);
                }
            }
            finally
            {
                CompleteFragmentUse(input);
            }
        }

        // Kept out of MaterializeOpacityComposite so that only the mask path allocates the callback's closure.
        private static void DrawMasked(
            OpacityMaskRenderFragmentPayload payload,
            IReadOnlyList<MaterializedRenderValue> values,
            ImmediateCanvas canvas)
            => _ = payload.Mask.Registry.Use(
                payload.Mask,
                mask =>
                {
                    using (canvas.PushOpacityMask(mask, payload.BrushBounds, payload.Invert))
                        DrawValues(values, canvas);
                    return true;
                });

        private IReadOnlyList<MaterializedRenderValue> ExecuteOpaque(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
            => ExecuteOnDeviceGrid(
                currentTarget,
                () => ExecuteOpaqueCore(fragment, currentTarget, requestedScale));

        private IReadOnlyList<MaterializedRenderValue> ExecuteOpaqueCore(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            var payload = (OpaqueRenderFragmentPayload)fragment.Payload!;
            OpaqueRenderDescription description = payload.Description;
            var flattened = new List<MaterializedRenderValue>();
            var inputReadbacks = new List<bool>();
            var inputRanges = new List<RenderExecutionInputRange>(fragment.Inputs.Length);
            EffectiveScale outputSupply = requestedScale
                ?? (!fragment.EffectiveScale.IsUnbounded
                    ? fragment.EffectiveScale
                    : EffectiveScale.At(currentTarget.Density));
            for (int inputIndex = 0; inputIndex < fragment.Inputs.Length; inputIndex++)
            {
                RenderFragmentReference input = fragment.Inputs[inputIndex];
                IReadOnlyList<MaterializedRenderValue> inputValues = Materialize(
                    input,
                    currentTarget,
                    input.EffectiveScale.IsUnbounded
                        ? ResolveOpaqueInputCallerScale(fragment, description, inputIndex, outputSupply)
                        : null);
                RenderInputReadback readback = payload.InputReadbacks[inputIndex];
                readback.ValidateRuntimeCount(input.ValueCardinality, inputValues.Count);
                inputRanges.Add(new RenderExecutionInputRange(flattened.Count, inputValues.Count));
                for (int valueIndex = 0; valueIndex < inputValues.Count; valueIndex++)
                {
                    flattened.Add(inputValues[valueIndex]);
                    inputReadbacks.Add(readback.RequiresValue(valueIndex));
                }
            }

            try
            {
                if (payload.Topology == OpaqueRenderTopology.Map)
                {
                    var mapped = new List<MaterializedRenderValue>();
                    for (int inputIndex = 0; inputIndex < flattened.Count; inputIndex++)
                    {
                        MaterializedRenderValue input = flattened[inputIndex];
                        Rect outputBounds = description.Bounds.TransformBounds([input.CompleteBounds]);
                        EffectiveScale outputScale = requestedScale
                            ?? description.Scale.Resolve(
                                [input.EffectiveScale],
                                outputBounds,
                                _options.OutputScale,
                                _options.MaxWorkingScale);
                        mapped.AddRange(InvokeOpaque(
                            fragment,
                            description,
                            [input],
                            [inputReadbacks[inputIndex]],
                            [new RenderExecutionInputRange(0, 1)],
                            outputBounds,
                            outputScale,
                            description.ValueCardinality));
                    }

                    return mapped;
                }

                Rect declaredBounds = description.Bounds.TransformBounds(
                    flattened.SelectToArray(static value => value.CompleteBounds));
                EffectiveScale declaredScale = requestedScale
                    ?? description.Scale.Resolve(
                        flattened.SelectToArray(static value => value.EffectiveScale),
                        declaredBounds,
                        _options.OutputScale,
                        _options.MaxWorkingScale);
                IReadOnlyList<MaterializedRenderValue> result = InvokeOpaque(
                    fragment,
                    description,
                    flattened,
                    inputReadbacks,
                    inputRanges,
                    declaredBounds,
                    declaredScale,
                    description.ValueCardinality);
                return result;
            }
            finally
            {
                foreach (RenderFragmentReference input in fragment.Inputs)
                    CompleteFragmentUse(input);
            }
        }

        private IReadOnlyList<MaterializedRenderValue> ExecuteEffectItem(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
            => ExecuteOnDeviceGrid(
                currentTarget,
                () => ExecuteEffectItemCore(fragment, currentTarget, requestedScale),
                normalizeGridPhase: fragment.Payload is FilterEffectSegmentRenderFragmentPayload payload
                                    && payload.StripsDeviceGridPhase);

        private IReadOnlyList<MaterializedRenderValue> ExecuteEffectItemCore(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            Rect requiredRegion = ResolveFragmentRequirement(fragment, fragment.Bounds);
            var payload = (FilterEffectSegmentRenderFragmentPayload)fragment.Payload!;
            if (FilterEffectSegmentDirectReplaySupport.CanMaterialize(fragment))
            {
                return ExecuteDirectSkiaFilterMaterialization(
                    fragment,
                    currentTarget,
                    payload,
                    requiredRegion,
                    requestedScale);
            }

            var inputs = new EffectRenderValues();
            EffectiveScale inputRequestScale = requestedScale
                ?? (!fragment.EffectiveScale.IsUnbounded
                    ? fragment.EffectiveScale
                    : EffectiveScale.At(currentTarget.Density));
            for (int index = 0; index < fragment.Inputs.Length; index++)
            {
                RenderFragmentReference input = fragment.Inputs[index];
                inputs.Append(Materialize(
                    input,
                    currentTarget,
                    input.EffectiveScale.IsUnbounded ? inputRequestScale : null));
            }

            try
            {
                return payload.Context.Registry.Use(
                    payload.Context,
                    effectContext =>
                    {
                        using EffectTargets targets = CreateEffectTargets(
                            inputs,
                            payload.TakesParts,
                            ResolveCallbackReadRegion(payload, inputs, requiredRegion, fragment.EffectiveScale.Value),
                            fragment.EffectiveScale.Value);
                        using var builder = new SKImageFilterBuilder();
                        using FilterEffectExecutor executor = CreateFilterEffectExecutor(
                            targets,
                            builder,
                            fragment.EffectiveScale.Value);
                        ApplyFilterEffects(
                            executor,
                            effectContext,
                            payload.WorkingScalePolicy.HasValue,
                            executor.WorkingScale,
                            executor.MaxWorkingScale,
                            target: null,
                            requiredRegion);

                        EffectTargets output = executor.CurrentTargets;
                        var result = new EffectRenderValues(output.Count);
                        IReadOnlyList<ClippedTextOccurrence> clipped = output.ClippedText;
                        int nextClipped = 0;
                        for (int i = 0; i < output.Count; i++)
                        {
                            result.CopyClippedTextAt(clipped, i, ref nextClipped);
                            EffectTarget target = output[i];
                            if (target.RenderTarget is not { } renderTarget)
                                continue;

                            // A part still stands for all of its value, as far as the effects moved it.
                            MaterializedRenderValue value = MaterializeEffectItemTarget(
                                target,
                                renderTarget,
                                target.CompleteBounds ?? target.Bounds);
                            value.TextPart = target.TextPart;
                            _ownedValues.Add(value);

                            // Cropping the input to the backward region leaves the surrounding output
                            // undefined, so the published value must not claim it.
                            Rect selectedBounds = value.Bounds.Intersect(requiredRegion);
                            if (selectedBounds.Width == 0 || selectedBounds.Height == 0)
                            {
                                ReleaseUnpublished(value);
                                continue;
                            }

                            if (selectedBounds != value.Bounds)
                            {
                                if (value.PreserveImperativeRasterPlacement
                                    || value.RasterBounds.Contains(value.CompleteBounds))
                                {
                                    // Preserve a complete backing so later effect-item effects can sample
                                    // the physical footprint while Bounds remains the selected output.
                                    value.Bounds = selectedBounds;
                                }
                                else
                                {
                                    MaterializedRenderValue cropped = CropValue(
                                        fragment,
                                        value,
                                        selectedBounds);
                                    ReleaseUnpublished(value);
                                    value = cropped;
                                }
                            }

                            result.Add(value);
                        }
                        result.CopyClippedTextAt(clipped, output.Count, ref nextClipped);

                        return (IReadOnlyList<MaterializedRenderValue>)result;
                    });
            }
            finally
            {
                foreach (RenderFragmentReference input in fragment.Inputs)
                    CompleteFragmentUse(input);
            }
        }

        /// <summary>
        /// The region a segment reads of its inputs, when it hands them to a callback that declares where it reads, or
        /// <see langword="null"/> otherwise.
        /// </summary>
        /// <remarks>
        /// The region analysis asks such a segment's inputs for only this region, so the segment can be handed part of a
        /// value. The callback can move what it is given across the frame, so the part has to hold the value over all of
        /// the region, where the value has no content too: a part that ended at the content's edge would carry that edge
        /// in with it, and a script after it would stretch the edge across the gap.
        /// </remarks>
        private static Rect? ResolveCallbackReadRegion(
            FilterEffectSegmentRenderFragmentPayload payload,
            IReadOnlyList<MaterializedRenderValue> inputs,
            Rect requiredRegion,
            float density)
        {
            if (!payload.HasCallbackFootprint)
                return null;

            Rect stream = default;
            foreach (MaterializedRenderValue input in inputs)
                stream = stream.Union(input.CompleteBounds);
            return EffectItemSamplingSupport.TryResolveRequiredInput(
                payload.BoundsItems,
                payload.StreamInputCount,
                requiredRegion,
                new EffectItemStream(stream, density),
                out Rect read)
                ? read
                : null;
        }

        // One effect target per value, spanning its complete bounds when its raster holds them and its bounds
        // otherwise. In a segment that takes parts, a part keeps the complete bounds of the value it stands for, and one
        // handed to a callback that declares where it reads spans the value within readRegion on the pixels the whole
        // value would have at density.
        private static EffectTargets CreateEffectTargets(
            IReadOnlyList<MaterializedRenderValue> inputs,
            bool takesParts,
            Rect? readRegion,
            float density = 1)
        {
            var targets = new EffectTargets();
            try
            {
                for (int index = 0; index < inputs.Count; index++)
                {
                    MaterializedRenderValue input = inputs[index];
                    bool hasCompleteBacking = input.RasterBounds.Contains(input.CompleteBounds);
                    Rect bounds = hasCompleteBacking ? input.CompleteBounds : input.Bounds;
                    Rect? completeBounds = null;
                    if (!hasCompleteBacking && takesParts)
                    {
                        completeBounds = input.CompleteBounds;
                        // The value holds nothing within its complete bounds outside its own, where the flush ahead of the
                        // callback leaves the part clear. The flush draws the part on the pixels the whole value would be
                        // drawn on, so that the callback moves the same pixels.
                        if (readRegion is { } read)
                        {
                            bounds = AlignWithin(
                                bounds.Union(read.Intersect(input.CompleteBounds)),
                                input.CompleteBounds,
                                density);
                        }
                    }

                    targets.Add(new EffectTarget(
                        input.Target,
                        bounds,
                        input.EffectiveScale,
                        input.DeviceBounds,
                        input.DeviceGridOffset,
                        input.PreserveImperativeRasterPlacement && hasCompleteBacking)
                    {
                        OriginalBounds = new Rect(default, bounds.Size),
                        Bounds = bounds,
                        CompleteBounds = completeBounds,
                        TextPart = input.TextPart,
                    });
                }

                if (inputs is EffectRenderValues { ClippedText: { } clipped })
                    targets.SetClippedText(clipped);

                return targets;
            }
            catch
            {
                targets.Dispose();
                throw;
            }
        }

        // Clipped occurrences have an order among paint values but never own a raster.
        private sealed class EffectRenderValues(int capacity = 0) : List<MaterializedRenderValue>(capacity)
        {
            public List<ClippedTextOccurrence>? ClippedText { get; set; }

            public static IReadOnlyList<ClippedTextOccurrence> GetClippedText(IReadOnlyList<MaterializedRenderValue> values)
                => values is EffectRenderValues { ClippedText: { } clipped } ? clipped : Array.Empty<ClippedTextOccurrence>();

            public void Append(IReadOnlyList<MaterializedRenderValue> values)
            {
                if (values is EffectRenderValues { ClippedText: { } clipped })
                {
                    var destination = ClippedText ??= [];
                    foreach (ClippedTextOccurrence entry in clipped)
                        destination.Add(entry with { TargetIndex = Count + entry.TargetIndex });
                }
                AddRange(values);
            }

            public void CopyClippedTextAt(IReadOnlyList<ClippedTextOccurrence> clipped, int index, ref int next)
            {
                while (next < clipped.Count && clipped[next].TargetIndex == index)
                    (ClippedText ??= []).Add(new ClippedTextOccurrence(Count, clipped[next++].Tag));
            }
        }

        // The smallest rectangle within whole that holds region and whose edges lie on the pixels a buffer for whole
        // has at density.
        private static Rect AlignWithin(Rect region, Rect whole, float density)
        {
            if (region.IsEmpty)
                return region;

            float left = whole.X + MathF.Floor((region.X - whole.X) * density) / density;
            float top = whole.Y + MathF.Floor((region.Y - whole.Y) * density) / density;
            float right = MathF.Min(whole.Right, whole.X + MathF.Ceiling((region.Right - whole.X) * density) / density);
            float bottom = MathF.Min(whole.Bottom, whole.Y + MathF.Ceiling((region.Bottom - whole.Y) * density) / density);
            return new Rect(left, top, right - left, bottom - top);
        }

        private FilterEffectExecutor CreateFilterEffectExecutor(
            EffectTargets targets,
            SKImageFilterBuilder builder,
            float workingScale)
            => new FilterEffectExecutor(
                targets,
                builder,
                new FilterEffectExecutionSettings(_options.Intent, _options.Purpose)
                {
                    OutputScale = _options.OutputScale,
                    WorkingScale = workingScale,
                    MaxWorkingScale = _options.MaxWorkingScale,
                    DeviceGridOffset = _activeDeviceGridOffset,
                    ProgramAcquirer = (target, source) => AcquireStandaloneProgram(
                        target,
                        source),
                    DrawableBrushMaterializer = _drawableBrushMaterializer,
                    UseExecutorManagedCanvas = true,
                    RenderTargetLeaseSession = _targets,
                    TargetDomain = _options.TargetDomain,
                    ShaderGraphProgramAcquirer = AcquireShaderGraphProgram,
                    ShaderGraphImages = _programCaches.ShaderGraphImages,
                });

        // Applies program and completes the executor's working-scale policy boundary. The program's callbacks get a
        // dependency context at the given scales, reaching target when there is one, that ends on return. Only
        // requiredOutput of the result is kept, so a shader graph at the end produces only that.
        private void ApplyFilterEffects(
            FilterEffectExecutor executor,
            FilterEffectContext program,
            bool materializationRequired,
            float workingScale,
            float maxWorkingScale,
            ImmediateCanvas? target,
            Rect requiredOutput)
        {
            var execution = new RenderExecutionContext(_renderDependency,
                workingScale, maxWorkingScale, target, _cancellationToken);
            executor.ExecutionContext = execution;
            try
            {
                executor.Apply(program, requiredOutput);
                executor.CompletePolicyBoundary(materializationRequired);
            }
            finally { execution.End(); }
        }

        private IReadOnlyList<MaterializedRenderValue> ExecuteDirectSkiaFilterMaterialization(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            FilterEffectSegmentRenderFragmentPayload payload,
            Rect requiredRegion,
            EffectiveScale? requestedScale)
        {
            RenderFragmentReference input = fragment.Inputs[0];
            if (requiredRegion.Width == 0 || requiredRegion.Height == 0)
            {
                CompleteFragmentUse(input);
                return [];
            }

            // The output is the value the plan compiled for this segment, so it is rasterized at the demand
            // Materialize resolved, not at the density of the target it happens to be drawn onto: a cache
            // capture of this value is checked against that planned density. It holds only the region the
            // request needs, which is what the density is fitted to, as the cache's identity fits it.
            float requestedDensity = requestedScale?.Value
                ?? (fragment.EffectiveScale.IsUnbounded
                    ? currentTarget.Density
                    : fragment.EffectiveScale.Value);
            EffectiveScale scale = ClampToActiveDeviceGrid(
                requiredRegion,
                EffectiveScale.At(requestedDensity));
            // The plan gave the replayed input this segment's demand, not the density of the buffer it is
            // drawn into, so the nested replay is checked against the former.
            EffectiveScale inputCallerScale = requestedScale ?? scale;
            MaterializedRenderValue? output = null;
            bool succeeded = false;
            bool replayStarted = false;
            try
            {
                output = CreateOwnedValue(
                    requiredRegion,
                    scale,
                    fragment.Bounds,
                    allowPreviewDrop: _previewDropEligibleMaterializations.Contains(fragment));
                output.TextPart = GetReplayTextPart(input);
                using var builder = new SKImageFilterBuilder();
                foreach (IFEItem item in payload.BoundsItems)
                    ((IFEItem_Skia)item).AcceptsDirect(builder);

                using var paint = builder.HasFilter()
                    ? new SKPaint { ImageFilter = builder.GetFilter() }
                    : null;
                using var canvas = CreateValueCanvas(output);
                using (canvas.PushTransform(output.RasterAlignmentTransform))
                {
                    if (paint is not null)
                    {
                        Rect replayedInputBounds = ResolveFragmentRequirement(input, input.Bounds);
                        Rect layerContentBounds = GetDirectFilterLayerBounds(replayedInputBounds);
                        using (canvas.PushBlendMode(BlendMode.SrcOver))
                        using (canvas.PushTransform(Matrix.Identity))
                        // The filter layer must match the region Replay writes, not the input's full
                        // semantic bounds. A wider layer exposes unwritten pixels to spatial filters.
                        using (canvas.PushFilterLayer(paint, layerContentBounds))
                        {
                            replayStarted = true;
                            Replay(input, canvas, inputCallerScale);
                        }
                    }
                    else
                    {
                        replayStarted = true;
                        Replay(input, canvas, inputCallerScale);
                    }
                }

                succeeded = true;
                return [output];
            }
            finally
            {
                if (!replayStarted)
                    CompleteFragmentUse(input);
                if (!succeeded && output is not null)
                    ReleaseUnpublished(output);
            }
        }

        private MaterializedRenderValue MaterializeEffectItemTarget(
            EffectTarget target,
            RenderTarget renderTarget,
            Rect completeBounds)
        {
            if (target.PreserveImperativeRasterPlacement)
            {
                Vector deviceGridOffset = target.Placement.DeviceBounds
                    .ToRect(target.Placement.Scale.Value)
                    .Position - target.Placement.RasterBounds.Position;
                return CreateOwnedEffectItemValue(
                    target,
                    renderTarget,
                    target.Bounds,
                    target.Placement.Scale,
                    target.Placement.DeviceBounds,
                    deviceGridOffset,
                    completeBounds,
                    preserveImperativeRasterPlacement: true);
            }

            Rect canonicalRasterBounds = target.Allocation.RasterBounds;
            PixelRect semanticDeviceBounds = target.Allocation.ToDevice(target.Bounds);
            if (target.Placement.RasterBounds == canonicalRasterBounds
                && target.Placement.DeviceBounds.Contains(semanticDeviceBounds))
            {
                return CreateOwnedEffectItemValue(
                    target,
                    renderTarget,
                    target.Bounds,
                    target.Placement.Scale,
                    target.Placement.DeviceBounds,
                    target.Allocation.DeviceGridOffset,
                    completeBounds: completeBounds);
            }

            return CreateNormalizedValue(
                renderTarget,
                target.Placement.RasterBounds,
                target.Bounds,
                target.Placement.Scale,
                target.Allocation.DeviceGridOffset,
                completeBounds,
                addRasterApron: false);
        }

        private static MaterializedRenderValue CreateOwnedEffectItemValue(
            EffectTarget effectTarget,
            RenderTarget renderTarget,
            Rect bounds,
            EffectiveScale effectiveScale,
            PixelRect deviceBounds,
            Vector deviceGridOffset,
            Rect? completeBounds = null,
            bool preserveImperativeRasterPlacement = false)
        {
            EffectTargetRenderTargetLease? renderTargetLease = effectTarget.TakeRenderTargetLease();
            if (renderTargetLease is null)
            {
                return CreateOwnedShallowCopy(
                    renderTarget,
                    bounds,
                    effectiveScale,
                    deviceBounds,
                    deviceGridOffset,
                    completeBounds,
                    preserveImperativeRasterPlacement);
            }

            try
            {
                return new MaterializedRenderValue(
                    renderTargetLease,
                    bounds,
                    effectiveScale,
                    deviceBounds,
                    deviceGridOffset,
                    completeBounds,
                    preserveImperativeRasterPlacement);
            }
            catch
            {
                renderTargetLease.Dispose();
                throw;
            }
        }

    }
}
