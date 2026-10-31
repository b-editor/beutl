using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering.Requests;

internal sealed partial class RenderRequestExecutor
{
    private void ExecuteFamily(
        CompiledRenderRequest request,
        ImmediateCanvas destination,
        Rect replayBounds,
        Action? finalizeOutput,
        RenderProgramCaches programCaches,
        ICollection<RenderRequestExecutionState> frames,
        ICollection<Exception> cleanupFailures,
        ref int nestedRootAcquisitions,
        ref bool nestedPreviewDropObserved)
    {
        foreach (CompiledRenderRequest nested in request.NestedRequests)
        {
            ExecuteNested(
                nested,
                destination,
                programCaches,
                frames,
                cleanupFailures,
                ref nestedRootAcquisitions,
                ref nestedPreviewDropObserved);
        }

        ExecuteSingle(
            request,
            destination,
            replayBounds,
            finalizeOutput,
            programCaches,
            frames,
            cleanupFailures,
            ref nestedPreviewDropObserved);
    }

    private void ExecuteNested(
        CompiledRenderRequest request,
        ImmediateCanvas fallbackDestination,
        RenderProgramCaches programCaches,
        ICollection<RenderRequestExecutionState> frames,
        ICollection<Exception> cleanupFailures,
        ref int nestedRootAcquisitions,
        ref bool nestedPreviewDropObserved,
        bool requireTarget = false)
    {
        NestedRenderTargetBinding binding = request.Request.Options.TargetBinding
            ?? throw new InvalidOperationException("A nested request has no separate-target binding.");
        bool needsTarget = requireTarget || request.Measurement.HasContributingValues
                           || request.Measurement.HasTargetEffects;
        if (!needsTarget)
        {
            ExecuteFamily(
                request,
                fallbackDestination,
                request.ExecutionTargetBounds,
                finalizeOutput: null,
                programCaches,
                frames,
                cleanupFailures,
                ref nestedRootAcquisitions,
                ref nestedPreviewDropObserved);
            return;
        }

        Rect bounds = request.Request.Options.TargetDomain
            ?? throw new InvalidOperationException(
                "A separate-target nested request requires a finite target domain.");

        RenderTargetLease? lease = null;
        ImmediateCanvas? canvas = null;
        FamilyExecutionException? failure = null;
        bool dropped = false;
        RenderTargetCleanupFailureCheckpoint cleanupCheckpoint =
            _targets.CaptureCleanupFailureCheckpoint();
        try
        {
            PixelRect deviceBounds = PixelRect.FromRect(bounds, request.Request.Options.OutputScale);
            Rect rasterBounds = deviceBounds.ToRect(request.Request.Options.OutputScale);
            try
            {
                // TryAcquire itself throws for a Delivery session, so a null result is a preview drop.
                RenderTargetLease? acquired = _targets.TryAcquire(deviceBounds.Size);
                if (acquired is null)
                {
                    dropped = true;
                    nestedPreviewDropObserved = true;
                    SkipNestedFamily(request);
                }
                else
                {
                    lease = acquired;
                    nestedRootAcquisitions++;
                    RenderTarget target = lease.Target;
                    binding.Stage(lease, bounds, request.Request.Options.OutputScale);
                    lease = null;
                    canvas = ImmediateCanvas.CreateExecutorManaged(
                        target,
                        request.Request.Options.OutputScale,
                        request.Request.Options.MaxWorkingScale,
                        rasterBounds.Size,
                        request.Request.Options.Intent,
                        deviceBounds.Position);
                    canvas.Clear();
                }
            }
            catch (Exception ex)
            {
                failure = new FamilyExecutionException(
                    ExceptionDispatchInfo.Capture(ex));
            }

            if (failure is null && !dropped)
            {
                using (canvas!.PushTransform(Matrix.CreateTranslation(
                           -rasterBounds.X,
                           -rasterBounds.Y)))
                {
                    ExecuteFamily(
                        request,
                        canvas,
                        request.ExecutionTargetBounds,
                        finalizeOutput: null,
                        programCaches,
                        frames,
                        cleanupFailures,
                        ref nestedRootAcquisitions,
                        ref nestedPreviewDropObserved);
                }

                canvas.CloseWithoutFlush();
                canvas = null;
                binding.PrepareForSampling();
            }
        }
        catch (FamilyExecutionException ex)
        {
            failure = ex;
        }
        finally
        {
            if (failure is not null)
                binding.Reject();

            try
            {
                canvas?.CloseWithoutFlush();
            }
            catch (Exception ex)
            {
                CleanupFailures.Append(cleanupFailures, ex);
                failure ??= new FamilyExecutionException(
                    ExceptionDispatchInfo.Capture(ex));
            }

            lease?.Dispose();
            foreach (Exception cleanupFailure in _targets.GetCleanupFailuresSince(cleanupCheckpoint))
            {
                CleanupFailures.Append(cleanupFailures, cleanupFailure);
                failure ??= new FamilyExecutionException(
                    ExceptionDispatchInfo.Capture(cleanupFailure));
            }
        }

        if (failure is not null)
            throw failure;
    }

    private void ExecuteSingle(
        CompiledRenderRequest request,
        ImmediateCanvas destination,
        Rect replayBounds,
        Action? finalizeOutput,
        RenderProgramCaches programCaches,
        ICollection<RenderRequestExecutionState> frames,
        ICollection<Exception> cleanupFailures,
        ref bool nestedPreviewDropObserved)
    {
        request.Request.TransitionTo(RenderRequestState.Executing);
        var state = new RenderRequestExecutionState(
            request.Request.Options,
            request.Graph,
            request.ExecutionPlan,
            request.TargetDependencies,
            request.Regions,
            request.Roots,
            request.MaterializationDemands,
            request.PreviewDropEligibleMaterializations,
            request.CacheResolution,
            _targets,
            programCaches,
            _shaderBackendPreference,
            _cancellationToken,
            (root, owner, identity, domain, scale) => ExecuteDependency(request, root, owner, identity, domain, scale,
                destination, programCaches, frames, cleanupFailures));
        if (nestedPreviewDropObserved)
            state.MarkPreviewAllocationDropped();

        frames.Add(state);
        using IDisposable materializerScope = destination.PushDrawableBrushMaterializer(
            state.DrawableBrushMaterializer);
        // Brush-owned intermediates allocate themselves, so they need the pass's session to reach the
        // caller's factory; without it a tile brush would mix a global-allocator surface into the pass.
        using IDisposable leaseSessionScope = destination.PushRenderTargetLeaseSession(_targets);
        ExceptionDispatchInfo? bodyFailure = null;
        try
        {
            if (replayBounds.Width != 0 && replayBounds.Height != 0)
            {
                Rect rasterClip = RenderScaleUtilities.AddRasterApron(
                        PixelRect.FromRect(replayBounds, destination.Density))
                    .ToRect(destination.Density);
                using (destination.PushClip(rasterClip))
                {
                    foreach (RenderFragmentReference root in request.Roots)
                        state.Replay(root, destination);
                }
            }
            else
            {
                foreach (RenderFragmentReference root in request.Roots)
                {
                    if (root.HasTargetEffects)
                        state.Replay(root, destination);
                }
            }

            _cancellationToken.ThrowIfCancellationRequested();
            state.ValidateExecutionCompleted(
                allowSkippedIslands: replayBounds.Width == 0 || replayBounds.Height == 0);
            state.PrepareBuiltInBackdropCaptures();
            finalizeOutput?.Invoke();
        }
        catch (Exception ex)
        {
            bodyFailure = ExceptionDispatchInfo.Capture(ex);
        }

        ExceptionDispatchInfo? cleanupFailure = null;
        try
        {
            state.DisposeNonCacheValues();
        }
        catch (Exception ex)
        {
            CleanupFailures.Append(cleanupFailures, ex);
            cleanupFailure = ExceptionDispatchInfo.Capture(
                ex is AggregateException aggregate
                    ? aggregate.Flatten().InnerExceptions[0]
                    : ex);
        }

        // A drop the body observed - a materialization that could not allocate, a replay that abandoned -
        // makes this request's output incomplete, and its parent composites that output. Reporting it only
        // for a failed root acquisition would let the parent publish degraded pixels into a cache.
        nestedPreviewDropObserved |= state.PreviewAllocationDropObserved;

        if (bodyFailure is not null)
            throw new FamilyExecutionException(bodyFailure);
        if (cleanupFailure is not null)
            throw new FamilyExecutionException(cleanupFailure);
    }

    private static void ValidateFamilyForExecution(CompiledRenderRequest request)
    {
        foreach (CompiledRenderRequest nested in request.NestedRequests)
            ValidateFamilyForExecution(nested);
        ObjectDisposedException.ThrowIf(request.IsDisposed, request);
        if (request.Request.State != RenderRequestState.Planned)
            throw new InvalidOperationException("Every render request in a family must be planned before execution.");
    }

    private static void CompleteFamily(CompiledRenderRequest request)
    {
        foreach (CompiledRenderRequest member in EnumerateFamilyDepthFirst(request))
            member.Request.TransitionTo(RenderRequestState.Completed);
    }

    // A dropped subtree never runs, but CompleteFamily still transitions it, and RenderRequest only allows
    // Planned -> Executing -> Completed.
    private static void SkipNestedFamily(CompiledRenderRequest request)
    {
        foreach (CompiledRenderRequest member in EnumerateFamilyDepthFirst(request))
        {
            member.Request.Options.TargetBinding?.Reject();
            member.Request.TransitionTo(RenderRequestState.Executing);
        }
    }

    private static void RejectNestedBindings(CompiledRenderRequest request)
    {
        foreach (CompiledRenderRequest member in EnumerateFamilyDepthFirst(request))
            member.Request.Options.TargetBinding?.Reject();
    }

    private static void FailFamily(CompiledRenderRequest request)
    {
        foreach (CompiledRenderRequest member in EnumerateFamilyDepthFirst(request))
            member.Request.FailFamilyMember();
    }

    private static IEnumerable<CompiledRenderRequest> EnumerateFamilyDepthFirst(
        CompiledRenderRequest request)
    {
        foreach (CompiledRenderRequest nested in request.NestedRequests)
        {
            foreach (CompiledRenderRequest member in EnumerateFamilyDepthFirst(nested))
                yield return member;
        }

        yield return request;
    }

    private static void EnsureOwnerPrimary(RenderRequestOwner owner, Exception? failure)
    {
        if (failure is not null && owner.PrimaryFailure is null)
            owner.RecordPrimaryFailure(failure);
    }

    private sealed class FamilyExecutionException(
        ExceptionDispatchInfo failure) : Exception
    {
        public ExceptionDispatchInfo Failure { get; } = failure;
    }

    private sealed class FamilyCachePublicationException(
        ExceptionDispatchInfo failure,
        IReadOnlyList<Exception> cleanupFailures) : Exception
    {
        public ExceptionDispatchInfo Failure { get; } = failure;

        public IReadOnlyList<Exception> CleanupFailures { get; } = cleanupFailures;
    }

    private sealed class PreviewAllocationDropException : Exception
    {
    }

    private static void RecordAdditionalFailures(
        RenderRequestOwner owner,
        IEnumerable<Exception> failures)
    {
        ImmutableArray<Exception> ownerCleanupFailures = owner.CleanupFailures;
        foreach (Exception failure in failures)
        {
            if (!ReferenceEquals(owner.PrimaryFailure?.SourceException, failure)
                && !ContainsSame(ownerCleanupFailures, failure))
            {
                owner.RecordPrimaryFailure(failure);
            }
        }

        static bool ContainsSame(ImmutableArray<Exception> failures, Exception failure)
        {
            for (int index = 0; index < failures.Length; index++)
            {
                if (ReferenceEquals(failures[index], failure))
                    return true;
            }

            return false;
        }
    }

}
