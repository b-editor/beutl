using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal sealed partial class RenderRequestExecutor
{
    private void ExecuteFamily(
        CompiledRenderRequest request,
        ImmediateCanvas destination,
        Rect replayBounds,
        Action? finalizeOutput,
        ProgramCache<CachedSkRuntimeEffect> programCache,
        ICollection<FamilyExecutionFrame> frames,
        ICollection<Exception> cleanupFailures,
        ref int nestedRootAcquisitions)
    {
        foreach (CompiledRenderRequest nested in request.NestedRequests)
        {
            ExecuteNested(
                nested,
                destination,
                programCache,
                frames,
                cleanupFailures,
                ref nestedRootAcquisitions);
        }

        ExecuteSingle(
            request,
            destination,
            replayBounds,
            finalizeOutput,
            programCache,
            frames,
            cleanupFailures);
    }

    private void ExecuteNested(
        CompiledRenderRequest request,
        ImmediateCanvas fallbackDestination,
        ProgramCache<CachedSkRuntimeEffect> programCache,
        ICollection<FamilyExecutionFrame> frames,
        ICollection<Exception> cleanupFailures,
        ref int nestedRootAcquisitions)
    {
        NestedRenderTargetBinding binding = request.Request.Options.TargetBinding
            ?? throw new InvalidOperationException("A nested request has no separate-target binding.");
        bool needsTarget = request.Measurement.HasContributingValues
                           || request.Measurement.HasTargetEffects;
        if (!needsTarget)
        {
            ExecuteFamily(
                request,
                fallbackDestination,
                request.ExecutionTargetBounds,
                finalizeOutput: null,
                programCache,
                frames,
                cleanupFailures,
                ref nestedRootAcquisitions);
            return;
        }

        Rect bounds = request.Request.Options.TargetDomain
            ?? throw new InvalidOperationException(
                "A separate-target nested request requires a finite target domain.");

        RenderTargetLease? lease = null;
        ImmediateCanvas? canvas = null;
        FamilyExecutionException? failure = null;
        RenderTargetCleanupFailureCheckpoint cleanupCheckpoint =
            _targets.CaptureCleanupFailureCheckpoint();
        try
        {
            PixelRect deviceBounds = PixelRect.FromRect(bounds, request.Request.Options.OutputScale);
            Rect rasterBounds = deviceBounds.ToRect(request.Request.Options.OutputScale);
            try
            {
                lease = _targets.Acquire(deviceBounds.Size);
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
            catch (Exception ex)
            {
                failure = new FamilyExecutionException(
                    ExceptionDispatchInfo.Capture(ex));
            }

            if (failure is null)
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
                        programCache,
                        frames,
                        cleanupFailures,
                        ref nestedRootAcquisitions);
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
                AppendCleanupFailures(cleanupFailures, ex);
                failure ??= new FamilyExecutionException(
                    ExceptionDispatchInfo.Capture(ex));
            }

            lease?.Dispose();
            foreach (Exception cleanupFailure in _targets.GetCleanupFailuresSince(cleanupCheckpoint))
            {
                AppendCleanupFailures(cleanupFailures, cleanupFailure);
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
        ProgramCache<CachedSkRuntimeEffect> programCache,
        ICollection<FamilyExecutionFrame> frames,
        ICollection<Exception> cleanupFailures)
    {
        request.Request.TransitionTo(RenderRequestState.Executing);
        var state = new CompatibilityExecutionState(
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
            programCache,
            _afterCaptureAllocation);
        var frame = new FamilyExecutionFrame(request, state);
        frames.Add(frame);
        using IDisposable materializerScope = destination.PushDrawableBrushMaterializer(
            state.DrawableBrushMaterializer);
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
            AppendCleanupFailures(cleanupFailures, ex);
            cleanupFailure = ExceptionDispatchInfo.Capture(
                ex is AggregateException aggregate
                    ? aggregate.Flatten().InnerExceptions[0]
                    : ex);
        }

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

    private sealed record FamilyExecutionFrame(
        CompiledRenderRequest Request,
        CompatibilityExecutionState State);

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

    private static void AppendCleanupFailures(
        ICollection<Exception> failures,
        Exception exception)
    {
        if (exception is AggregateException aggregate)
        {
            foreach (Exception inner in aggregate.Flatten().InnerExceptions)
            {
                AddCleanupFailure(failures, inner);
            }
        }
        else
        {
            AddCleanupFailure(failures, exception);
        }
    }

    private static void AddCleanupFailure(
        ICollection<Exception> failures,
        Exception exception)
    {
        if (failures.Any(existing => ReferenceEquals(existing, exception)))
            return;
        failures.Add(exception);
    }

    private static void RecordAdditionalFailures(
        RenderRequestOwner owner,
        IEnumerable<Exception> failures)
    {
        Exception[] ownerCleanupFailures = [.. owner.CleanupFailures];
        foreach (Exception failure in failures)
        {
            if (!ReferenceEquals(owner.PrimaryFailure?.SourceException, failure)
                && !ownerCleanupFailures.Any(existing => ReferenceEquals(existing, failure)))
            {
                owner.RecordPrimaryFailure(failure);
            }
        }
    }

}
