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
        private void ExecuteTargetCommand(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            var payload = (TargetCommandRenderFragmentPayload)fragment.Payload!;
            TargetCommandDescription description = payload.Description;
            var values = new List<MaterializedRenderValue>();
            var inputReadbacks = new List<bool>();
            var inputRanges = new List<RenderExecutionInputRange>(fragment.Inputs.Length);
            for (int inputIndex = 0; inputIndex < fragment.Inputs.Length; inputIndex++)
            {
                IReadOnlyList<MaterializedRenderValue> inputValues = Materialize(
                    fragment.Inputs[inputIndex],
                    destination);
                RenderInputReadback readback = payload.InputReadbacks[inputIndex];
                readback.ValidateRuntimeCount(
                    fragment.Inputs[inputIndex].ValueCardinality,
                    inputValues.Count);
                inputRanges.Add(new RenderExecutionInputRange(values.Count, inputValues.Count));
                for (int valueIndex = 0; valueIndex < inputValues.Count; valueIndex++)
                {
                    values.Add(inputValues[valueIndex]);
                    inputReadbacks.Add(readback.RequiresValue(valueIndex));
                }
            }

            var images = new List<SKImage>(values.Count);
            Bitmap? targetSnapshot = null;
            RenderExecutionSessionToken token = CreateExecutionSessionToken();
            try
            {
                token.RunAndComplete(
                    () =>
                    {
                        IReadOnlyList<RenderExecutionInput> inputs = CreateTargetCommandExecutionInputs(
                            token,
                            values,
                            inputReadbacks,
                            images);
                        Rect affectedBounds = ResolveTargetRegion(
                            description.AffectedRegion,
                            fragment,
                            destination);
                        Rect requiredRegion = ResolveTargetAccessRequirement(fragment, affectedBounds);
                        if (description.Access == TargetAccess.Readback
                            && (requiredRegion.Width == 0 || requiredRegion.Height == 0))
                        {
                            // A readback-only root has no pixel-writing output requirement, but its
                            // authored callback still consumes the immutable preceding target token.
                            requiredRegion = affectedBounds;
                        }
                        CallbackCanvasCapability capability = description.AffectedRegion.Kind switch
                        {
                            TargetRegionKind.Empty => CallbackCanvasCapability.TargetCommandEmpty,
                            TargetRegionKind.Region => CallbackCanvasCapability.TargetCommandRegion,
                            TargetRegionKind.Full => CallbackCanvasCapability.TargetCommandFull,
                            _ => throw new InvalidOperationException("The target-command region is uninitialized."),
                        };
                        RenderCallbackCanvas callbackCanvas = RenderCallbackCanvas.CreateTargetAttached(
                            token,
                            requiredRegion,
                            destination,
                            capability);
                        var session = new TargetCommandSession(
                            token,
                            inputs,
                            inputRanges,
                            affectedBounds,
                            requiredRegion,
                            _options.Intent,
                            _options.Purpose,
                            callbackCanvas,
                            description.Resources,
                            description.Access == TargetAccess.Readback,
                            description.Access == TargetAccess.Readback
                                ? () => TakeTargetSnapshot(ref targetSnapshot)
                                : null);
                        if (description.Access == TargetAccess.Readback)
                        {
                            RecordSynchronization();
                            targetSnapshot = SnapshotTarget(destination, requiredRegion);
                        }
                        description.Execute(session);
                        session.ValidateCompletion();
                    });
            }
            finally
            {
                targetSnapshot?.Dispose();
                foreach (SKImage image in images)
                    image.Dispose();
                foreach (RenderFragmentReference input in fragment.Inputs)
                    CompleteFragmentUse(input);
            }
        }

        private void ExecuteRawTargetCommand(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            RawTargetCommandDescription description =
                ((RawTargetCommandRenderFragmentPayload)fragment.Payload!).Description;
            using ImmediateCanvas view = destination.CreateExecutionView();
            RenderExecutionSessionToken token = CreateExecutionSessionToken();
            token.RunAndComplete(
                () =>
                {
                    token.UseRawCanvas(
                        view,
                        canvas =>
                        {
                            description.Execute(new RawTargetCommandSession(
                                token,
                                canvas,
                                _options.Intent,
                                _options.Purpose,
                                description.Resources));
                        });
                });
        }

        private void ExecuteTargetScope(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            TargetScopeDescription description =
                ((TargetScopeRenderFragmentPayload)fragment.Payload!).Description;
            RenderFragmentReference input = fragment.Inputs.Single();
            RenderExecutionSessionToken token = CreateExecutionSessionToken();
            token.RunAndComplete(
                () =>
                {
                    Rect? parentDomain = fragment.Id is { } id
                        && _resolvedParentScopeDomains.TryGetValue(id, out Rect resolvedParent)
                            ? resolvedParent
                            : _options.TargetDomain;
                    Rect callbackBounds = TargetWriteMetadataResolver.Resolve(fragment, parentDomain)
                        ?? fragment.Bounds;
                    Rect requiredRegion = ResolveFragmentRequirement(fragment, callbackBounds);
                    RenderCallbackCanvas callbackCanvas = RenderCallbackCanvas.CreateTargetAttached(
                        token,
                        requiredRegion,
                        destination,
                        CallbackCanvasCapability.TargetScope);
                    var session = new TargetScopeSession(
                        token,
                        fragment.Bounds,
                        requiredRegion,
                        _options.Intent,
                        _options.Purpose,
                        callbackCanvas,
                        description.Resources,
                        canvas => Replay(input, canvas));
                    description.Execute(session);
                    session.ValidateCompletion();
                });
        }

        private IReadOnlyList<MaterializedRenderValue> MaterializeValueReplayMap(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            if (fragment.Inputs.Length != 1
                || !fragment.ValueCardinality.Equals(RenderValueCardinality.Single))
            {
                throw new InvalidOperationException(
                    "A value replay map requires exactly one single-value input stream.");
            }

            Rect requiredRegion = ResolveFragmentRequirement(fragment, fragment.Bounds);
            if (requiredRegion.Width == 0 || requiredRegion.Height == 0)
            {
                CompleteFragmentUse(fragment.Inputs[0]);
                return [];
            }

            float requestedDensity = requestedScale?.Value
                ?? (fragment.EffectiveScale.IsUnbounded
                    ? currentTarget.Density
                    : fragment.EffectiveScale.Value);
            float density = RenderMaterializationDensityPolicy.Clamp(
                fragment,
                requestedDensity);
            density = ClampToActiveDeviceGrid(
                    fragment.Bounds,
                    EffectiveScale.At(density),
                    requiresRasterApron: true)
                .Value;
            EffectiveScale scale = EffectiveScale.At(density);
            PixelRect deviceBounds = RenderScaleUtilities.AddRasterApron(
                PixelRect.FromRect(requiredRegion, density));
            MaterializedRenderValue output = CreateOwnedValue(
                requiredRegion,
                scale,
                fragment.Bounds,
                deviceBounds,
                allowPreviewDrop: _previewDropEligibleMaterializations.Contains(fragment));
            bool succeeded = false;
            try
            {
                using var canvas = CreateValueCanvas(output);
                canvas.Clear();
                using (canvas.PushTransform(output.RasterAlignmentTransform))
                {
                    ExecuteTargetScope(fragment, canvas);
                }

                succeeded = true;
                return [output];
            }
            finally
            {
                if (!succeeded)
                    ReleaseUnpublished(output);
            }
        }

        private void ExecuteRawTargetScope(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            RawTargetScopeDescription description =
                ((RawTargetScopeRenderFragmentPayload)fragment.Payload!).Description;
            RenderFragmentReference input = fragment.Inputs.Single();
            using ImmediateCanvas view = destination.CreateExecutionView();
            RenderExecutionSessionToken token = CreateExecutionSessionToken();
            token.RunAndComplete(
                () =>
                {
                    token.UseRawCanvas(
                        view,
                        canvas =>
                        {
                            var session = new RawTargetScopeSession(
                                token,
                                canvas,
                                fragment.Bounds,
                                _options.Intent,
                                _options.Purpose,
                                description.Resources,
                                replayCanvas => replayCanvas.ReplayTargetScopeInput(
                                    nested => Replay(input, nested)));
                            description.Execute(session);
                            session.ValidateCompletion();
                        });
                });
        }

        private IReadOnlyList<RenderExecutionInput> CreateExecutionInputs(
            RenderExecutionSessionToken token,
            IReadOnlyList<MaterializedRenderValue> values,
            bool requiresReadback,
            List<SKImage> images)
        {
            var inputs = new List<RenderExecutionInput>(values.Count);
            foreach (MaterializedRenderValue value in values)
            {
                SKImage image = value.Target.Value.Snapshot();
                images.Add(image);
                Func<Bitmap>? createSnapshot = requiresReadback
                    ? () => SnapshotInputForReadback(value)
                    : null;
                inputs.Add(new RenderExecutionInput(
                    token,
                    value.Bounds,
                    value.EffectiveScale,
                    value.DeviceBounds,
                    value.RasterBounds,
                    image,
                    createSnapshot,
                    requiresReadback));
            }

            return inputs;
        }

        private IReadOnlyList<RenderExecutionInput> CreateTargetCommandExecutionInputs(
            RenderExecutionSessionToken token,
            IReadOnlyList<MaterializedRenderValue> values,
            IReadOnlyList<bool> inputReadbacks,
            List<SKImage> images)
        {
            if (inputReadbacks.Count != values.Count)
                throw new InvalidOperationException("Target-command input readback planning did not reconcile.");

            var inputs = new List<RenderExecutionInput>(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                MaterializedRenderValue value = values[index];
                SKImage image = value.Target.Value.Snapshot();
                images.Add(image);
                bool requiresReadback = inputReadbacks[index];
                Func<Bitmap>? createSnapshot = requiresReadback
                    ? () => SnapshotInputForReadback(value)
                    : null;
                inputs.Add(new RenderExecutionInput(
                    token,
                    value.Bounds,
                    value.EffectiveScale,
                    value.DeviceBounds,
                    value.RasterBounds,
                    image,
                    createSnapshot,
                    requiresReadback));
            }

            return inputs;
        }

        private Bitmap SnapshotInputForReadback(MaterializedRenderValue value)
        {
            RecordSynchronization();
            return value.Target.Snapshot();
        }

        private Rect ResolveFragmentRequirement(
            RenderFragmentReference fragment,
            Rect completeBounds)
            => _regions.GetFragmentRequirement(fragment)
                .Resolve(completeBounds)
                .Intersect(completeBounds);

        private Rect ResolveTargetAccessRequirement(
            RenderFragmentReference fragment,
            Rect completeBounds)
            => _regions.GetTargetAccessRequirement(fragment)
                .Resolve(completeBounds)
                .Intersect(completeBounds);

        private Rect ResolveTargetRegion(
            TargetRegion region,
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            return region.Kind switch
            {
                TargetRegionKind.Empty => Rect.Empty,
                TargetRegionKind.Region => region.Value,
                TargetRegionKind.Full
                    when fragment.Id is { } id
                         && _resolvedAccessDomains.TryGetValue(id, out Rect domain) => domain,
                TargetRegionKind.Full when _options.TargetDomain is { } domain => domain,
                TargetRegionKind.Full => new Rect(default, destination.LogicalSize),
                _ => throw new InvalidOperationException("The target region is uninitialized."),
            };
        }

        private static Bitmap TakeTargetSnapshot(ref Bitmap? snapshot)
        {
            Bitmap result = snapshot
                ?? throw new InvalidOperationException("The target snapshot was already consumed.");
            snapshot = null;
            return result;
        }

        private static Bitmap SnapshotTarget(
            ImmediateCanvas destination,
            Rect requiredRegion)
        {
            using RenderTarget target = RenderTarget.GetRenderTarget(destination);
            using Bitmap snapshot = target.Snapshot();
            PixelRect targetBounds = new(0, 0, snapshot.Width, snapshot.Height);
            PixelRect sourceRegion = PixelRect.FromRect(
                    requiredRegion.TransformToAABB(destination.Transform),
                    1)
                .Intersect(targetBounds);
            if (sourceRegion.Width == 0 || sourceRegion.Height == 0)
            {
                throw new InvalidOperationException(
                    "A target readback requirement must resolve to a non-empty region on the current target.");
            }

            return snapshot.ExtractSubset(sourceRegion);
        }

        private IReadOnlyList<MaterializedRenderValue> MaterializeLayer(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget,
            EffectiveScale? requestedScale)
        {
            if (_values.TryGetValue(fragment, out IReadOnlyList<MaterializedRenderValue>? existing))
                return existing;

            Rect domain = ((LayerRenderFragmentPayload)fragment.Payload!).Domain
                ?? fragment.Bounds;
            EffectiveScale scale = ClampToActiveDeviceGrid(
                fragment.Bounds,
                requestedScale ?? ResolveConcreteScale(fragment));
            Vector? deviceGridOffset = RequiresLocalDestructiveDeviceGrid(fragment)
                ? default(Vector)
                : null;
            MaterializedRenderValue value = CreateOwnedValue(
                domain,
                scale,
                deviceGridOffset: deviceGridOffset,
                allowPreviewDrop: _previewDropEligibleMaterializations.Contains(fragment));
            bool succeeded = false;
            try
            {
                using (var canvas = CreateValueCanvas(value))
                using (canvas.PushTransform(value.RasterAlignmentTransform))
                {
                    if (fragment.Inputs.Length == 1
                        && IsMatchingTargetLayerScope(fragment.Inputs[0], canvas, domain))
                    {
                        // The finite Layer already provides the target-layer isolation surface.
                        ReplayTargetLayerScopeIntoExistingLayer(
                            fragment.Inputs[0],
                            canvas,
                            currentTarget);
                    }
                    else
                    {
                        int backdropSourceCount = _backdropSources.Count;
                        if (backdropSourceCount != 0)
                            _backdropSources.Add(currentTarget);
                        try
                        {
                            foreach (RenderFragmentReference input in fragment.Inputs)
                                Replay(input, canvas);
                        }
                        finally
                        {
                            RemoveBackdropSources(backdropSourceCount);
                        }
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

        private IReadOnlyList<MaterializedRenderValue> CaptureTarget(
            RenderFragmentReference fragment,
            ImmediateCanvas currentTarget)
        {
            TargetCaptureDescription description = fragment.Payload switch
            {
                TargetCaptureRenderFragmentPayload payload => payload.Description,
                BuiltInBackdropCaptureRenderFragmentPayload payload => payload.Description,
                _ => throw new InvalidOperationException("The target-capture payload is invalid."),
            };
            Rect bounds = fragment.Kind == RenderFragmentKind.BuiltInBackdropCapture
                ? fragment.Bounds
                : description.Bounds;
            EffectiveScale scale = description.Scale.PreservesTargetSupply
                ? EffectiveScale.At(DeviceGridAlignment.ResolveLocalDensity(currentTarget))
                : ResolveConcreteScale(fragment);
            scale = ClampToActiveDeviceGrid(bounds, scale);
            MaterializedRenderValue value = CreateOwnedValue(
                bounds,
                scale,
                allowPreviewDrop: _previewDropEligibleMaterializations.Contains(fragment));
            bool succeeded = false;
            try
            {
                _afterCaptureAllocation?.Invoke(fragment.Kind);
                using (var canvas = CreateValueCanvas(value))
                using (canvas.PushTransform(value.RasterAlignmentTransform))
                {
                    canvas.ClipRect(bounds);
                    bool capturesBackingTarget = fragment.Id is { } fragmentId
                        && _regions.BackingTargetBackdropCaptures.Contains(fragmentId);
                    if (fragment.Kind == RenderFragmentKind.BuiltInBackdropCapture)
                    {
                        foreach (ImmediateCanvas backdropSource in _backdropSources)
                            DrawTargetIntoCapture(backdropSource, canvas, capturesBackingTarget);
                    }

                    DrawTargetIntoCapture(currentTarget, canvas, capturesBackingTarget);
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

        private void ReplayTargetLayerScope(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            Rect domain = ResolveTargetLayerScopeDomain(fragment, destination);
            if (domain.Width == 0 || domain.Height == 0)
                return;

            EffectiveScale scale = EffectiveScale.At(destination.Density);
            Vector deviceGridOffset = RequiresLocalDestructiveDeviceGrid(fragment)
                ? default
                : DeviceGridAlignment.ResolveLogicalOffset(destination);
            scale = ClampToDeviceGrid(domain, scale, deviceGridOffset);
            MaterializedRenderValue value = CreateOwnedValue(
                domain,
                scale,
                deviceGridOffset: deviceGridOffset);
            try
            {
                using (var canvas = CreateValueCanvas(value))
                using (canvas.PushTransform(value.RasterAlignmentTransform))
                {
                    int backdropSourceCount = _backdropSources.Count;
                    _backdropSources.Add(destination);
                    try
                    {
                        foreach (RenderFragmentReference input in fragment.Inputs)
                            Replay(input, canvas);
                    }
                    finally
                    {
                        RemoveBackdropSources(backdropSourceCount);
                    }
                }

                DrawValue(value, destination);
            }
            finally
            {
                ReleaseUnpublished(value);
            }
        }

        private Rect ResolveTargetLayerScopeDomain(
            RenderFragmentReference fragment,
            ImmediateCanvas destination)
        {
            TargetRegion region = ((TargetLayerScopeRenderFragmentPayload)fragment.Payload!).Region;
            return region.Kind switch
            {
                TargetRegionKind.Empty => Rect.Empty,
                TargetRegionKind.Region => region.Value,
                TargetRegionKind.Full
                    when fragment.Id is { } id
                         && _resolvedScopeDomains.TryGetValue(id, out Rect resolved) => resolved,
                TargetRegionKind.Full when _options.TargetDomain is { } targetDomain => targetDomain,
                TargetRegionKind.Full => new Rect(default, destination.LogicalSize),
                _ => throw new InvalidOperationException("The target-layer region is uninitialized."),
            };
        }

        private bool IsMatchingTargetLayerScope(
            RenderFragmentReference fragment,
            ImmediateCanvas destination,
            Rect domain)
            => fragment.Kind == RenderFragmentKind.TargetLayerScope
               && ResolveTargetLayerScopeDomain(fragment, destination) == domain;

        private void ReplayTargetLayerScopeIntoExistingLayer(
            RenderFragmentReference scope,
            ImmediateCanvas destination,
            ImmediateCanvas backdropSource)
        {
            ExecuteReplayIsland(
                scope,
                () =>
                {
                    int backdropSourceCount = _backdropSources.Count;
                    _backdropSources.Add(backdropSource);
                    try
                    {
                        foreach (RenderFragmentReference input in scope.Inputs)
                            Replay(input, destination);
                    }
                    finally
                    {
                        RemoveBackdropSources(backdropSourceCount);
                    }
                });
        }

        private static void DrawTargetIntoCapture(
            ImmediateCanvas sourceCanvas,
            ImmediateCanvas captureCanvas,
            bool capturesBackingTarget)
        {
            using RenderTarget source = RenderTarget.GetRenderTarget(sourceCanvas);
            if (capturesBackingTarget)
            {
                float sourceDensity = sourceCanvas.SurfaceDensity;
                captureCanvas.DrawRenderTargetScaledWithoutFlush(
                    source,
                    new Rect(
                        sourceCanvas.DeviceOrigin.X / sourceDensity,
                        sourceCanvas.DeviceOrigin.Y / sourceDensity,
                        source.Width / sourceDensity,
                        source.Height / sourceDensity));
                return;
            }

            // Target-local captures are authored in the target's logical space, so place the surface
            // through the inverse of its transform. A singular transform has no visible local pixels.
            if (!DeviceGridAlignment.TryResolveSurfaceToLogical(sourceCanvas, out Matrix toLocal))
                return;

            using (captureCanvas.PushTransform(toLocal))
            {
                captureCanvas.DrawRenderTargetScaledWithoutFlush(
                    source,
                    new Rect(0, 0, source.Width, source.Height));
            }
        }

        private void RemoveBackdropSources(int count)
        {
            if (_backdropSources.Count > count)
                _backdropSources.RemoveRange(count, _backdropSources.Count - count);
        }

        private static bool RequiresLocalDestructiveDeviceGrid(RenderFragmentReference fragment)
        {
            if (fragment.Kind == RenderFragmentKind.Blend
                && fragment.Payload is BlendRenderFragmentPayload payload
                && BlendModeRenderNode.RequiresFullTargetRegion(payload.BlendMode))
            {
                return payload.BlendMode switch
                {
                    BlendMode.DstIn => fragment.Inputs.Any(RequiresLocalDestructiveDeviceGrid),
                    BlendMode.DstOut => !CanReplayWithDirectDstOut(fragment.Inputs.Single()),
                    _ => true,
                };
            }

            return fragment.Inputs.Any(RequiresLocalDestructiveDeviceGrid);
        }

        private static bool CanReplayWithDirectDstOut(RenderFragmentReference fragment)
        {
            return fragment.Kind switch
            {
                RenderFragmentKind.OpaqueSource
                    => ((OpaqueRenderFragmentPayload)fragment.Payload!).Description.SupportsDirectDstOut,
                RenderFragmentKind.TargetScope
                    => fragment.Inputs.All(CanReplayWithDirectDstOut),
                RenderFragmentKind.Opacity
                    => ((OpacityRenderFragmentPayload)fragment.Payload!).Opacity == 1f
                       && fragment.Inputs.All(CanReplayWithDirectDstOut),
                _ => false,
            };
        }

    }
}
