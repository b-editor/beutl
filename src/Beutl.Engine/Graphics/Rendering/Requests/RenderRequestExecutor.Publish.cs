using System.Collections.Immutable;
using System.Runtime.ExceptionServices;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Media;
using SkiaSharp;

namespace Beutl.Graphics.Rendering.Requests;

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

        var cleanupFailures = new List<Exception>();
        RejectNestedBindings(request);

        RenderRequestOwner owner = request.Request.Options.Owner;
        int ownerCleanupStart = owner.CleanupFailures.Length;
        owner.Cleanup();
        ImmutableArray<Exception> ownerCleanupFailures = owner.CleanupFailures;
        for (int index = ownerCleanupStart; index < ownerCleanupFailures.Length; index++)
            AppendCleanupFailures(cleanupFailures, ownerCleanupFailures[index]);

        try
        {
            // Close the session early so cleanup failures are finalized before the family completes;
            // the enclosing owner may dispose it again because session disposal is idempotent.
            _targets.Dispose();
            _targets.ThrowIfCleanupFailed();
        }
        catch (Exception ex)
        {
            AppendCleanupFailures(cleanupFailures, ex);
        }

        if (cleanupFailures.Count != 0)
        {
            Exception primaryFailure = cleanupFailures[0];
            EnsureOwnerPrimary(owner, primaryFailure);
            RecordAdditionalFailures(owner, cleanupFailures);
            FailFamily(request);
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        Statistics = default;
        CompleteFamily(request);
    }

    private static IReadOnlyList<Exception> PublishCacheCapturesAtomically(
        IReadOnlyList<RenderRequestExecutionState> frames)
    {
        HashSet<RenderNodeCache>? seenCaches = null;
        bool hasPublications = false;
        foreach (RenderRequestExecutionState state in frames)
            hasPublications |= state.ValidateCacheCaptures(ref seenCaches);

        if (!hasPublications)
        {
            foreach (RenderRequestExecutionState state in frames)
                state.AcceptCacheCaptures();
            return [];
        }

        var transferredTargets = new List<RenderTarget>();
        var publications = new List<RenderNodeCachePublication>();
        IReadOnlyList<Exception> replacedStorageCleanupFailures;
        try
        {
            foreach (RenderRequestExecutionState state in frames)
                state.AppendCachePublications(publications, transferredTargets);
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
        foreach (RenderRequestExecutionState state in frames)
            state.AcceptCacheCaptures();
        return replacedStorageCleanupFailures;
    }

    private static RenderExecutionStatistics AggregateStatistics(
        IEnumerable<RenderRequestExecutionState> frames,
        int nestedRootAcquisitions)
    {
        int shaderRuns = 0;
        int shaderStages = 0;
        int fusedRuns = 0;
        int spirvRuns = 0;
        int intermediateTargets = nestedRootAcquisitions;
        int programCacheHits = 0;
        int synchronizations = 0;
        foreach (RenderRequestExecutionState state in frames)
        {
            RenderExecutionStatistics statistics = state.CreateStatistics();
            shaderRuns += statistics.ShaderRunExecutions;
            shaderStages += statistics.ShaderStageExecutions;
            fusedRuns += statistics.FusedShaderRunExecutions;
            spirvRuns += statistics.SpirvShaderRunExecutions;
            intermediateTargets += statistics.IntermediateTargetAcquisitions;
            programCacheHits += statistics.ProgramCacheHits;
            synchronizations += statistics.Synchronizations;
        }

        return new RenderExecutionStatistics(
            shaderRuns,
            shaderStages,
            fusedRuns,
            spirvRuns,
            intermediateTargets,
            programCacheHits,
            synchronizations);
    }

}
