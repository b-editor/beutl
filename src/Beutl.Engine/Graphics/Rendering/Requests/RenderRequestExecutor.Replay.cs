using System.Collections.Immutable;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shaders;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private sealed partial class RenderRequestExecutionState
    {
        private bool TryReplayEngineSourceDirect(
            RenderFragmentReference fragment,
            ImmediateCanvas destination,
            EffectiveScale callerScale)
        {
            OpaqueRenderDescription description =
                ((OpaqueRenderFragmentPayload)fragment.Payload!).Description;
            if (description.DirectReplay is not { } replay
                || !IsSingleUseLiveContribution(fragment))
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
                ? callerScale
                : fragment.EffectiveScale;
            try
            {
                for (int inputIndex = 0; inputIndex < fragment.Inputs.Length; inputIndex++)
                {
                    RenderFragmentReference input = fragment.Inputs[inputIndex];
                    inputs.AddRange(Materialize(
                        input,
                        destination,
                        input.EffectiveScale.IsUnbounded
                            ? ResolveOpaqueInputCallerScale(fragment, description, inputIndex, outputSupply)
                            : null));
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
                                    _ = CreateExecutionInputs(
                                        token,
                                        inputs,
                                        requiresReadback: false,
                                        images);
                                    using (destination.BeginDirectExecution(token))
                                    {
                                        replay(new EngineDirectRenderSession(
                                            token,
                                            destination));
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
            ImmediateCanvas destination,
            EffectiveScale callerScale)
        {
            // The Vulkan-native path consumes and produces pooled RGBA16F textures. Keep it behind the ordinary
            // materialization boundary instead of recording GPU work directly into a Skia replay destination.
            if (ShouldDeferDirectReplayToSpirv(run))
                return false;

            RenderFragmentReference output = run.GetOutput(_graph);
            RenderFragmentReference inputFragment = run.GetInput(_graph);
            if (!ReferenceEquals(output, fragment)
                || _replayDepth != 1
                || !IsSingleUseLiveContribution(fragment))
            {
                return false;
            }

            if (!DirectShaderRunPlanner.TryResolve(
                    fragment,
                    run,
                    _graph,
                    _regions,
                    DirectRenderTargetGeometry.FromCanvas(destination),
                    out DirectShaderRunPlan directPlan))
            {
                return false;
            }

            EffectiveScale inputRequestScale = ResolveShaderRunInputCallerScale(
                run,
                !output.EffectiveScale.IsUnbounded ? output.EffectiveScale : callerScale);
            IReadOnlyList<MaterializedRenderValue> inputs = Materialize(
                inputFragment,
                destination,
                inputFragment.EffectiveScale.IsUnbounded ? inputRequestScale : null);
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
                        ShaderRunDestination.ForDirect(destination, directPlan),
                        directPlan.OutputBounds,
                        directPlan.RequiredRegion));
                return true;
            }
            finally
            {
                CompleteFragmentUse(inputFragment);
            }
        }

        private bool TryReplayBuiltInSkiaFilterChainDirect(
            RenderFragmentReference fragment,
            ImmediateCanvas destination,
            EffectiveScale callerScale)
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

            // The demand resolver hands each segment's input the segment's own demand, and a segment with a
            // concrete scale of its own restarts that chain there, so the scale the base input is asked for
            // is the outer caller's only until the first concrete link.
            EffectiveScale inputCallerScale = callerScale;
            foreach ((RenderFragmentReference segment, _) in chain)
            {
                if (!segment.EffectiveScale.IsUnbounded)
                    inputCallerScale = segment.EffectiveScale;
            }

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
                        ? inputCallerScale
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
                    Replay(input, destination, inputCallerScale);
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
            if (fragment.Inputs.Length != 1
                || !IsSingleUseLiveContribution(fragment)
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

        // A value contribution can be replayed straight into the destination only while nothing else reads it:
        // it is not materialized yet, no selected cache producer keeps it, and this is its one remaining use.
        private bool IsSingleUseLiveContribution(RenderFragmentReference fragment)
            => fragment.ContributesValuesToTarget
               && !_values.ContainsKey(fragment)
               && !(fragment.Id is { } id && _cacheResolution.HasSelectedProducer(id))
               && _resourceUses.GetRemainingUseCount(fragment) == 1;

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
            ImmediateCanvas destination,
            EffectiveScale callerScale)
        {
            IReadOnlyList<MaterializedRenderValue> values = Materialize(
                fragment,
                destination,
                fragment.EffectiveScale.IsUnbounded
                    ? callerScale
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

    }
}
