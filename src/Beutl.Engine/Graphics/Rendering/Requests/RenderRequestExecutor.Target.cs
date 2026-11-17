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

            var images = new List<SurfaceSnapshot.Lease>(values.Count);
            Bitmap? targetSnapshot = null;
            RenderExecutionSessionToken token = CreateExecutionSessionToken();
            try
            {
                token.RunAndComplete(
                    () =>
                    {
                        List<RenderExecutionInput> inputs = CreateTargetCommandExecutionInputs(
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
                            inputs,
                            inputRanges,
                            affectedBounds,
                            _options.Intent,
                            _options.Purpose,
                            callbackCanvas,
                            description.Resources,
                            description.Access == TargetAccess.Readback
                                ? () => TakeTargetSnapshot(ref targetSnapshot)
                                : null,
                            (program, policy) => ExecuteTargetFilterEffects(program, policy, values, destination, requiredRegion));
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
                foreach (SurfaceSnapshot.Lease snapshot in images)
                    snapshot.Dispose();
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
            ImmediateCanvas destination,
            EffectiveScale callerScale)
        {
            TargetScopeDescription description =
                ((TargetScopeRenderFragmentPayload)fragment.Payload!).Description;
            RenderFragmentReference input = fragment.Inputs.Single();
            // Only a scope whose transform lives in the input's own coordinates carries the caller's demand
            // back through its scale contract; an ambient-target scope already has that scale in the
            // destination matrix. This mirrors the demand resolver's replay-input step.
            EffectiveScale inputCallerScale =
                description.TransformSpace == RenderScopeTransformSpace.InputLogical
                    ? ResolveScopeInputCallerScale(description.Scale, callerScale)
                    : callerScale;
            RenderExecutionSessionToken token = CreateExecutionSessionToken();
            token.RunAndComplete(
                () =>
                {
                    Rect? parentDomain = fragment.Id is { } id
                        && _resolvedExecutionDomains?.TryGetValue(id, out Rect resolvedParent) == true
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
                        fragment.Bounds,
                        _options.Intent,
                        _options.Purpose,
                        callbackCanvas,
                        description.Resources,
                        canvas => Replay(input, canvas, inputCallerScale));
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
                    ExecuteTargetScope(fragment, canvas, requestedScale ?? scale);
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
            ImmediateCanvas destination,
            EffectiveScale callerScale)
        {
            RawTargetScopeDescription description =
                ((RawTargetScopeRenderFragmentPayload)fragment.Payload!).Description;
            RenderFragmentReference input = fragment.Inputs.Single();
            // A raw scope's declared scale contract is the only statement of how its replayed input is
            // consumed, so its backward map applies unconditionally, as it does in the demand resolver.
            EffectiveScale inputCallerScale = ResolveScopeInputCallerScale(description.Scale, callerScale);
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
                                    nested => Replay(input, nested, inputCallerScale)));
                            description.Execute(session);
                            session.ValidateCompletion();
                        });
                });
        }

        private List<RenderExecutionInput> CreateTargetCommandExecutionInputs(
            RenderExecutionSessionToken token,
            IReadOnlyList<MaterializedRenderValue> values,
            IReadOnlyList<bool> inputReadbacks,
            List<SurfaceSnapshot.Lease> images)
        {
            if (inputReadbacks.Count != values.Count)
                throw new InvalidOperationException("Target-command input readback planning did not reconcile.");

            var inputs = new List<RenderExecutionInput>(values.Count);
            for (int index = 0; index < values.Count; index++)
            {
                MaterializedRenderValue value = values[index];
                var snapshot = SurfaceSnapshot.Take(value.Target.Value);
                images.Add(snapshot);
                bool requiresReadback = inputReadbacks[index];
                inputs.Add(CreateExecutionInput(token, value, snapshot.Image, requiresReadback));
            }

            return inputs;
        }

        private RenderExecutionInput CreateExecutionInput(
            RenderExecutionSessionToken token,
            MaterializedRenderValue value,
            SKImage image,
            bool requiresReadback)
        {
            Func<Bitmap>? createSnapshot = requiresReadback
                ? () => SnapshotInputForReadback(value)
                : null;
            return new RenderExecutionInput(
                token,
                value.Bounds,
                value.EffectiveScale,
                value.DeviceBounds,
                value.RasterBounds,
                image,
                createSnapshot);
        }

        private Bitmap SnapshotInputForReadback(MaterializedRenderValue value)
        {
            RecordSynchronization();
            return value.Target.Snapshot();
        }

        private Rect ResolveFragmentRequirement(
            RenderFragmentReference fragment,
            Rect completeBounds)
            => _regions.GetFragmentRequirement(fragment).ResolveWithin(completeBounds);

        private Rect ResolveTargetAccessRequirement(
            RenderFragmentReference fragment,
            Rect completeBounds)
            => _regions.GetTargetAccessRequirement(fragment).ResolveWithin(completeBounds);

        /// <summary>
        /// Allocates the value a layer or a layer scope draws into: only the part of <paramref name="domain"/> the
        /// request needs, at the density and on the device grid the whole domain would have.
        /// </summary>
        /// <remarks>
        /// A layer spans its content's complete extent, which keeps growing with particles that travel past the
        /// frame or a picture zoomed far into, while the request reads only what its consumers need: the frame and
        /// the margins their effects and filtering add. On the same grid, every pixel the part holds is the one the
        /// whole layer would hold. The whole domain is kept when the requirement is unknown or empty, and when
        /// something inside reads the layer's own pixels back, since such a read is not part of what the layer's
        /// consumers require of it.
        /// </remarks>
        private MaterializedRenderValue CreateLayerValue(
            RenderFragmentReference fragment,
            Rect domain,
            EffectiveScale scale,
            Vector? deviceGridOffset,
            bool allowPreviewDrop = false)
        {
            if (ResolveLayerPart(fragment, domain, scale.Value, deviceGridOffset ?? _activeDeviceGridOffset)
                is not { } part)
            {
                return CreateOwnedValue(
                    domain,
                    scale,
                    deviceGridOffset: deviceGridOffset,
                    allowPreviewDrop: allowPreviewDrop);
            }

            return CreateOwnedValue(
                part.Bounds,
                scale,
                completeBounds: domain,
                physicalDeviceBounds: part.DeviceBounds,
                deviceGridOffset: deviceGridOffset,
                physicalDeviceBoundsAreAligned: true,
                allowPreviewDrop: allowPreviewDrop);
        }

        private (Rect Bounds, PixelRect DeviceBounds)? ResolveLayerPart(
            RenderFragmentReference fragment,
            Rect domain,
            float scale,
            Vector gridOffset)
        {
            if (fragment.Id is not { } id
                || !_regions.FragmentRequirements.TryGetValue(id, out RequiredRegion requirement))
            {
                return null;
            }

            if (_regions.TargetAccessRequirements.TryGetValue(id, out RequiredRegion targetRequirement))
                requirement = requirement.Union(targetRequirement);
            if (requirement.IsEmpty || requirement.IsFull)
                return null;

            Rect required = requirement.ResolveWithin(domain);
            if (required.Width <= 0 || required.Height <= 0)
                return null;

            PixelRect whole = PixelRect.FromRect(domain.Translate(gridOffset), scale);
            PixelRect needed = PixelRect.FromRect(required.Translate(gridOffset), scale);
            if (needed.Width <= 0 || needed.Height <= 0)
                return null;

            // A requirement mapped back through a transform can round a pixel past the domain's own edge.
            PixelRect window = AllocationWindow(needed, whole.Union(needed));
            if (DeviceBoundsValidation.Covers(window, whole) || ReadsOwnTarget(fragment))
                return null;

            return (required, window);
        }

        // The pool reuses a target only of the very size it asks for, so a part that moves or grows a little with a
        // camera has to keep asking for the same size, as ShaderGraphRegions windows a graph's images.
        private static PixelRect AllocationWindow(PixelRect needed, PixelRect whole)
        {
            const int step = 256;
            int width = Math.Min((needed.Width + step - 1) / step * step, whole.Width);
            int height = Math.Min((needed.Height + step - 1) / step * step, whole.Height);
            return new PixelRect(
                Math.Max(Math.Min(needed.X, whole.Right - width), whole.X),
                Math.Max(Math.Min(needed.Y, whole.Bottom - height), whole.Y),
                width,
                height);
        }

        // A part's window can reach a pixel past the domain's own edge, where the whole layer's buffer would have
        // ended and cut its content off; the same pixels have to stay out of the part.
        private static void ClipToLayerDomain(ImmediateCanvas canvas, MaterializedRenderValue value)
        {
            if (value.Bounds != value.CompleteBounds)
                canvas.ClipRectCoveringDevicePixels(value.CompleteBounds);
        }

        // Whether a fragment's inputs read back the target they draw into, through a capture, a readback command or
        // raw target work. A readback command snapshots all of its affected region, which can lie past the part a
        // request needs.
        private bool ReadsOwnTarget(RenderFragmentReference fragment)
        {
            _ownTargetReaders ??= new Dictionary<RenderFragmentReference, bool>(ReferenceEqualityComparer.Instance);
            if (_ownTargetReaders.TryGetValue(fragment, out bool known))
                return known;

            bool reads = false;
            foreach (RenderFragmentReference input in fragment.Inputs)
            {
                if (input.Kind is RenderFragmentKind.TargetCapture
                        or RenderFragmentKind.BuiltInBackdropCapture
                        or RenderFragmentKind.RawTargetScope
                        or RenderFragmentKind.RawTargetCommand
                    || input.Payload is TargetCommandRenderFragmentPayload
                    {
                        Description.Access: TargetAccess.Readback,
                    }
                    || ReadsOwnTarget(input))
                {
                    reads = true;
                    break;
                }
            }

            _ownTargetReaders[fragment] = reads;
            return reads;
        }

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
                         && _resolvedExecutionDomains?.TryGetValue(id, out Rect domain) == true => domain,
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
            Rect domain = ((LayerRenderFragmentPayload)fragment.Payload!).Domain
                ?? fragment.Bounds;
            EffectiveScale scale = ClampToActiveDeviceGrid(
                fragment.Bounds,
                requestedScale ?? ResolveConcreteScale(fragment));
            Vector? deviceGridOffset = RequiresLocalDestructiveDeviceGrid(fragment)
                ? default(Vector)
                : null;
            MaterializedRenderValue value = CreateLayerValue(
                fragment,
                domain,
                scale,
                deviceGridOffset,
                allowPreviewDrop: _previewDropEligibleMaterializations.Contains(fragment));
            bool succeeded = false;
            try
            {
                using (var canvas = CreateValueCanvas(value))
                using (canvas.PushTransform(value.RasterAlignmentTransform))
                {
                    ClipToLayerDomain(canvas, value);
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
                        int backdropSourceCount = _backdropSources?.Count ?? 0;
                        if (backdropSourceCount != 0)
                            _backdropSources!.Add(currentTarget);
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
                using (var canvas = CreateValueCanvas(value))
                using (canvas.PushTransform(value.RasterAlignmentTransform))
                {
                    canvas.ClipRect(bounds);
                    bool capturesBackingTarget = fragment.Id is { } fragmentId
                        && _regions.BackingTargetBackdropCaptures.Contains(fragmentId);
                    if (fragment.Kind == RenderFragmentKind.BuiltInBackdropCapture)
                    {
                        if (_backdropSources is not null)
                        {
                            foreach (ImmediateCanvas backdropSource in _backdropSources)
                            {
                                if (_excludedBackdropSources?.Contains(backdropSource.Canvas) != true)
                                    DrawTargetIntoCapture(backdropSource, canvas, capturesBackingTarget);
                            }
                        }
                    }

                    if (fragment.Kind != RenderFragmentKind.BuiltInBackdropCapture
                        || _excludedBackdropSources?.Contains(currentTarget.Canvas) != true)
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
            ImmediateCanvas destination,
            EffectiveScale callerScale)
        {
            Rect domain = ResolveTargetLayerScopeDomain(fragment, destination);
            if (domain.Width == 0 || domain.Height == 0)
                return;

            EffectiveScale scale = EffectiveScale.At(destination.Density);
            Vector deviceGridOffset = RequiresLocalDestructiveDeviceGrid(fragment)
                ? default
                : DeviceGridAlignment.ResolveLogicalOffset(destination);
            scale = ClampToDeviceGrid(domain, scale, deviceGridOffset);
            MaterializedRenderValue value = CreateLayerValue(fragment, domain, scale, deviceGridOffset);
            try
            {
                using (var canvas = CreateValueCanvas(value))
                using (canvas.PushTransform(value.RasterAlignmentTransform))
                {
                    ClipToLayerDomain(canvas, value);
                    ReplayScopeInputs(fragment, canvas, destination, callerScale);
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
            => ResolveTargetRegion(
                ((TargetLayerScopeRenderFragmentPayload)fragment.Payload!).Region,
                fragment,
                destination);

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
                () => ReplayScopeInputs(scope, destination, backdropSource, callerScale: null));
        }

        // Replays a target layer scope's inputs into target, with backdropSource beneath them for the backdrop
        // captures they take. A scope that does not contribute to the backdrop keeps target out of those captures.
        // Without a caller scale, each input is replayed at the scale target's density asks of it.
        private void ReplayScopeInputs(
            RenderFragmentReference scope,
            ImmediateCanvas target,
            ImmediateCanvas backdropSource,
            EffectiveScale? callerScale)
        {
            int backdropSourceCount = _backdropSources?.Count ?? 0;
            (_backdropSources ??= []).Add(backdropSource);
            bool excludeBackdrop = !((TargetLayerScopeRenderFragmentPayload)scope.Payload!).ContributesToBackdrop
                && (_excludedBackdropSources ??= []).Add(target.Canvas);
            try
            {
                foreach (RenderFragmentReference input in scope.Inputs)
                {
                    if (callerScale is { } scale)
                        Replay(input, target, scale);
                    else
                        Replay(input, target);
                }
            }
            finally
            {
                RemoveBackdropSources(backdropSourceCount);
                if (excludeBackdrop) _excludedBackdropSources!.Remove(target.Canvas);
            }
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
            if (_backdropSources is null || _backdropSources.Count <= count)
                return;

            _backdropSources.RemoveRange(count, _backdropSources.Count - count);
            if (_backdropSources.Count == 0)
                _backdropSources = null;
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
