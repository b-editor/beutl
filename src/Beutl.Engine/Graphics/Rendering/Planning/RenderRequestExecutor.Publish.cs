using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering;

internal sealed partial class RenderRequestExecutor
{
    public void CompleteEmptySelection(CompiledRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(request.IsDisposed, request);
        if (request.SelectedOutputBounds.Width != 0 && request.SelectedOutputBounds.Height != 0)
        {
            throw new InvalidOperationException(
                "Only a request with an empty selected output can complete without execution.");
        }

        CompleteNoOp(request);
    }

    public void CompleteNoOp(CompiledRenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ObjectDisposedException.ThrowIf(request.IsDisposed, request);

        ValidateFamilyForExecution(request);
        foreach (CompiledRenderRequest member in EnumerateFamilyDepthFirst(request))
            member.Request.TransitionTo(RenderRequestState.Executing);

        RenderPipelineDiagnosticRecorder? rootDiagnostics = RenderRequestDiagnostics.TryGet(request.Request);
        var cleanupFailures = new List<Exception>();
        RejectNestedBindings(request);

        RenderRequestOwner owner = request.Request.Options.Owner;
        int ownerCleanupStart = owner.CleanupFailures.Length;
        owner.Cleanup();
        foreach (Exception failure in owner.CleanupFailures.Skip(ownerCleanupStart))
            AppendCleanupFailures(cleanupFailures, rootDiagnostics, failure);

        try
        {
            // Close the session early so cleanup failures are finalized before the family completes;
            // the enclosing owner may dispose it again because session disposal is idempotent.
            _targets.Dispose();
            _targets.ThrowIfCleanupFailed();
        }
        catch (Exception ex)
        {
            AppendCleanupFailures(cleanupFailures, rootDiagnostics, ex);
        }

        if (cleanupFailures.Count != 0)
        {
            Exception primaryFailure = cleanupFailures[0];
            EnsureOwnerPrimary(owner, primaryFailure);
            RecordAdditionalFailures(owner, cleanupFailures);
            FailFamily(request, RenderPipelineFailurePhase.Cleanup);
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        Statistics = default;
        CompleteFamily(request);
    }

    private static IReadOnlyList<Exception> PublishCacheCapturesAtomically(
        IReadOnlyList<FamilyExecutionFrame> frames)
    {
        var seenCaches = new HashSet<RenderNodeCache>(ReferenceEqualityComparer.Instance);
        foreach (FamilyExecutionFrame frame in frames)
            frame.State.ValidateCacheCaptures(seenCaches);

        var transferredTargets = new List<RenderTarget>();
        var publications = new List<RenderNodeCachePublication>();
        IReadOnlyList<Exception> replacedStorageCleanupFailures;
        try
        {
            foreach (FamilyExecutionFrame frame in frames)
                frame.State.AppendCachePublications(publications, transferredTargets);
            // Transfer only detaches targets from the renderer pool. No cache is observable until this
            // batch reaches PublishAtomically's validated reference-assignment commit point. If preparation
            // fails, the catch below disposes every detached target and leaves every node cache unchanged.
            replacedStorageCleanupFailures = RenderNodeCache.PublishAtomically(publications);
        }
        catch (Exception ex)
        {
            ExceptionDispatchInfo primary = ExceptionDispatchInfo.Capture(ex);
            var cleanupFailures = new List<Exception>();
            for (int index = transferredTargets.Count - 1; index >= 0; index--)
            {
                try
                {
                    transferredTargets[index].Dispose();
                }
                catch (Exception cleanupFailure)
                {
                    cleanupFailures.Add(cleanupFailure);
                }
            }
            throw new FamilyCachePublicationException(primary, cleanupFailures);
        }

        transferredTargets.Clear();
        foreach (FamilyExecutionFrame frame in frames)
            frame.State.AcceptCacheCaptures();
        return replacedStorageCleanupFailures;
    }

    private static RenderExecutionStatistics AggregateStatistics(
        IEnumerable<FamilyExecutionFrame> frames,
        int nestedRootAcquisitions)
    {
        int shaderRuns = 0;
        int shaderStages = 0;
        int fusedRuns = 0;
        int intermediateTargets = nestedRootAcquisitions;
        int programCacheHits = 0;
        int synchronizations = 0;
        foreach (FamilyExecutionFrame frame in frames)
        {
            RenderExecutionStatistics statistics = frame.State.CreateStatistics();
            shaderRuns += statistics.ShaderRunExecutions;
            shaderStages += statistics.ShaderStageExecutions;
            fusedRuns += statistics.FusedShaderRunExecutions;
            intermediateTargets += statistics.IntermediateTargetAcquisitions;
            programCacheHits += statistics.ProgramCacheHits;
            synchronizations += statistics.Synchronizations;
        }

        return new RenderExecutionStatistics(
            shaderRuns,
            shaderStages,
            fusedRuns,
            intermediateTargets,
            programCacheHits,
            synchronizations);
    }

}
