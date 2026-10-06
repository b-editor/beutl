using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private sealed record PendingPullRecoveryData(
        int Version,
        string Id,
        string CheckpointRef,
        string CheckpointCommit,
        string BranchRef,
        string BaseCommit,
        string TargetCommit,
        string ProjectFile,
        DateTimeOffset CreatedAt);

    private async Task<PendingPullRecovery> PersistPendingPullRecoveryCoreAsync(
        ProjectCheckpoint checkpoint,
        CheckedOutBranchTip targetTip,
        string projectFile,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        await ValidateCheckpointAsync(repository, runner, checkpoint, cancellationToken)
            .ConfigureAwait(false);
        ValidateAttachedBranchTip(targetTip, nameof(targetTip));
        if (!string.Equals(
                targetTip.RefName,
                checkpoint.BaseTip.RefName,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The recovery target must identify the checkpoint's local branch.",
                nameof(targetTip));
        }

        string? resolvedTarget = await TryResolveCommitAsync(
                repository,
                runner,
                targetTip.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                resolvedTarget,
                targetTip.Commit,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The recovery target must resolve to an existing commit.",
                nameof(targetTip));
        }

        string relativeProjectFile = GetRecoveryProjectFile(repository, projectFile);
        string absoluteProjectFile = GetLexicalRecoveryProjectFile(
            repository,
            relativeProjectFile);
        ValidateRecoveryProjectFilePhysicalContainment(repository, absoluteProjectFile);
        string id = Guid.NewGuid().ToString("N");
        string descriptorRef = GetPendingRecoveryRefPrefix(repository) + id;
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        var data = new PendingPullRecoveryData(
            PendingPullRecoveryFormatVersion,
            id,
            checkpoint.RefName,
            checkpoint.Commit,
            checkpoint.BaseTip.RefName,
            checkpoint.BaseTip.Commit,
            targetTip.Commit,
            relativeProjectFile,
            createdAt);
        string json = JsonSerializer.Serialize(data, s_recoveryJsonOptions);
        GitCommandResult descriptorObjectResult = await runner.RunAsync(
            repository,
            ["hash-object", "-w", "--stdin"],
            new GitCommandOptions(
                GitCommandExecutionKind.Local,
                StandardInput: json),
            cancellationToken).ConfigureAwait(false);
        string descriptorObject = descriptorObjectResult.Stdout.Trim();
        GitRevisionValidator.ValidateCommitId(descriptorObject, nameof(descriptorObject));

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await runner.RunAsync(
                repository,
                [
                    "update-ref",
                    "--create-reflog",
                    "-m",
                    "beutl pending pull recovery",
                    descriptorRef,
                    descriptorObject,
                    string.Empty,
                ],
                GitCommandOptions.Local,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception publicationException)
        {
            string? observedObject;
            try
            {
                observedObject = await TryResolveObjectAsync(
                        repository,
                        runner,
                        descriptorRef,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception observationException)
            {
                throw new AggregateException(
                    "The pending pull recovery publication failed and its durable result could not be observed.",
                    publicationException,
                    observationException);
            }

            if (!string.Equals(
                    observedObject,
                    descriptorObject,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (observedObject is null)
                {
                    throw;
                }

                throw new PendingPullRecoveryChangedException(descriptorRef);
            }
        }

        return new PendingPullRecovery(
            id,
            descriptorRef,
            descriptorObject,
            checkpoint,
            targetTip,
            absoluteProjectFile,
            createdAt);
    }

    private async Task<IReadOnlyList<PendingPullRecovery>> GetPendingPullRecoveriesCoreAsync(
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        GitCommandResult refs = await runner.RunAsync(
            repository,
            [
                "for-each-ref",
                "--sort=refname",
                "--format=%(refname)%00%(objectname)",
                GetPendingRecoveryRefPrefix(repository),
            ],
            new GitCommandOptions(
                GitCommandExecutionKind.Local,
                MaxStdoutBytes: MaxPendingRecoveryListBytes),
            cancellationToken).ConfigureAwait(false);
        if (refs.StdoutTruncated)
        {
            throw new InvalidOperationException(
                "The pending pull recovery list exceeded the safe output limit.");
        }

        var result = new List<PendingPullRecovery>();
        foreach (string rawLine in refs.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string line = rawLine.TrimEnd('\r');
            string[] fields = line.Split('\0');
            if (fields.Length != 2)
            {
                _logger.LogWarning(
                    "Ignored a malformed pending pull recovery ref record in {RepositoryRoot}.",
                    repository.RepoRoot);
                continue;
            }

            try
            {
                PendingPullRecovery? recovery = await ReadPendingPullRecoveryAsync(
                        repository,
                        runner,
                        fields[0],
                        fields[1],
                        cancellationToken)
                    .ConfigureAwait(false);
                if (recovery is not null)
                {
                    result.Add(recovery);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Ignored invalid pending pull recovery descriptor {RecoveryRef}.",
                    fields[0]);
            }
        }

        return result
            .OrderBy(static recovery => recovery.CreatedAt)
            .ThenBy(static recovery => recovery.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<PendingPullRecovery?> ReadPendingPullRecoveryAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string descriptorRef,
        string descriptorObject,
        CancellationToken cancellationToken)
    {
        string prefix = GetPendingRecoveryRefPrefix(repository);
        if (!descriptorRef.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        string id = descriptorRef[prefix.Length..];
        if (!Guid.TryParseExact(id, "N", out _)
            || id.Contains('/', StringComparison.Ordinal))
        {
            return null;
        }

        GitRevisionValidator.ValidateCommitId(descriptorObject, nameof(descriptorObject));
        GitCommandResult descriptor = await runner.RunAsync(
            repository,
            ["cat-file", "blob", descriptorObject],
            new GitCommandOptions(
                GitCommandExecutionKind.Local,
                MaxStdoutBytes: MaxPendingRecoveryDescriptorBytes),
            cancellationToken).ConfigureAwait(false);
        if (descriptor.StdoutTruncated)
        {
            return null;
        }

        PendingPullRecoveryData? data = JsonSerializer.Deserialize<PendingPullRecoveryData>(
            descriptor.Stdout,
            s_recoveryJsonOptions);
        if (data is null
            || data.Version != PendingPullRecoveryFormatVersion
            || !string.Equals(data.Id, id, StringComparison.Ordinal))
        {
            return null;
        }

        var checkpoint = new ProjectCheckpoint(
            data.CheckpointRef,
            data.CheckpointCommit,
            new CheckedOutBranchTip(data.BranchRef, data.BaseCommit));
        var targetTip = new CheckedOutBranchTip(data.BranchRef, data.TargetCommit);
        ValidateCheckpointRef(repository, checkpoint);
        ValidateAttachedBranchTip(targetTip, nameof(data.TargetCommit));
        if (!string.Equals(
                checkpoint.BaseTip.RefName,
                targetTip.RefName,
                StringComparison.Ordinal)
            || data.CreatedAt == default)
        {
            return null;
        }

        string projectFile = GetLexicalRecoveryProjectFile(repository, data.ProjectFile);
        await ValidateCheckpointAsync(repository, runner, checkpoint, cancellationToken)
            .ConfigureAwait(false);

        return new PendingPullRecovery(
            id,
            descriptorRef,
            descriptorObject,
            checkpoint,
            targetTip,
            projectFile,
            data.CreatedAt);
    }

    private async Task<PendingPullRecoveryOutcome> RecoverPendingPullRecoveryCoreAsync(
        PendingPullRecovery recovery,
        CancellationToken cancellationToken)
    {
        EnsureWorktreeMutationAllowed();
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        await ValidatePendingPullRecoveryAsync(
                repository,
                runner,
                recovery,
                cancellationToken)
            .ConfigureAwait(false);
        CheckedOutBranchTip actualTip = await GetCheckedOutBranchTipCoreAsync(
                repository,
                runner,
                cancellationToken)
            .ConfigureAwait(false);
        if (EqualsBranchTip(actualTip, recovery.TargetTip))
        {
            WorktreeStateFingerprint actualState = await CaptureWorktreeStateAsync(
                    repository,
                    runner,
                    recovery.TargetTip.Commit,
                    ".",
                    cancellationToken)
                .ConfigureAwait(false);
            string targetTree = await ResolveTreeAsync(
                    repository,
                    runner,
                    recovery.TargetTip.Commit,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!string.Equals(
                    actualState.Tree,
                    targetTree,
                    StringComparison.OrdinalIgnoreCase)
                || !await IsWholeRepositoryCleanAsync(repository, runner, cancellationToken)
                    .ConfigureAwait(false))
            {
                throw await CreatePreservedRecoveryExceptionAsync(
                        repository,
                        runner,
                        recovery,
                        new InvalidOperationException(
                            "The pulled branch tip is present, but its worktree state cannot be verified."))
                    .ConfigureAwait(false);
            }

            TreeTransitionResult rollback;
            try
            {
                rollback = await ApplyTreeTransitionAsync(
                        repository,
                        runner,
                        recovery.TargetTip,
                        recovery.Checkpoint.BaseTip,
                        recovery.TargetTip.Commit,
                        recovery.Checkpoint.BaseTip.Commit,
                        "beutl roll back pending pull target",
                        indexPlan: null,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw await CreatePreservedRecoveryExceptionAsync(
                        repository,
                        runner,
                        recovery,
                        ex)
                    .ConfigureAwait(false);
            }

            if (rollback.Outcome != TreeTransitionOutcome.AppliedTarget)
            {
                throw await CreatePreservedRecoveryExceptionAsync(
                        repository,
                        runner,
                        recovery,
                        rollback.Error
                        ?? new InvalidOperationException(
                            "The pulled branch could not be rolled back safely."))
                    .ConfigureAwait(false);
            }

            try
            {
                await RestoreProjectCheckpointCoreAsync(
                        recovery.Checkpoint,
                        CancellationToken.None,
                        validatePreparedTarget: () =>
                            ValidateRecoveryProjectFilePhysicalContainment(
                                repository,
                                recovery.ProjectFile))
                    .ConfigureAwait(false);
                ValidateRecoveryProjectFilePhysicalContainment(
                    repository,
                    recovery.ProjectFile);
                return PendingPullRecoveryOutcome.RestoredOriginal;
            }
            catch (Exception ex)
            {
                throw await CreatePreservedRecoveryExceptionAsync(
                        repository,
                        runner,
                        recovery,
                        ex)
                    .ConfigureAwait(false);
            }
        }

        if (!EqualsBranchTip(actualTip, recovery.Checkpoint.BaseTip))
        {
            string recoveryBranchName;
            try
            {
                recoveryBranchName = await PreserveCheckpointOnRecoveryBranchAsync(
                        repository,
                        runner,
                        recovery,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw await CreateCheckpointPreservationExceptionAsync(
                        repository,
                        runner,
                        recovery,
                        ex)
                    .ConfigureAwait(false);
            }

            try
            {
                if (!await TryReapplyCheckpointToExternallyOwnedTipAsync(
                        repository,
                        runner,
                        recovery,
                        actualTip,
                        CancellationToken.None)
                    .ConfigureAwait(false))
                {
                    throw new PendingPullRecoveryPreservedException(recoveryBranchName);
                }

                ValidateRecoveryProjectFilePhysicalContainment(
                    repository,
                    recovery.ProjectFile);
                return PendingPullRecoveryOutcome.ReappliedCheckpoint;
            }
            catch (PendingPullRecoveryPreservedException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new PendingPullRecoveryPreservedException(recoveryBranchName, ex);
            }
        }

        try
        {
            await RestoreProjectCheckpointCoreAsync(
                    recovery.Checkpoint,
                    CancellationToken.None,
                    validatePreparedTarget: () =>
                        ValidateRecoveryProjectFilePhysicalContainment(
                            repository,
                            recovery.ProjectFile))
                .ConfigureAwait(false);
            CheckedOutBranchTip recoveredTip = await GetCheckedOutBranchTipCoreAsync(
                    repository,
                    runner,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (!EqualsBranchTip(recoveredTip, recovery.Checkpoint.BaseTip))
            {
                throw new InvalidOperationException(
                    "The repository branch changed while the pending pull recovery was restored.");
            }

            ValidateRecoveryProjectFilePhysicalContainment(
                repository,
                recovery.ProjectFile);
            return PendingPullRecoveryOutcome.RestoredOriginal;
        }
        catch (Exception ex)
        {
            throw await CreatePreservedRecoveryExceptionAsync(
                    repository,
                    runner,
                    recovery,
                    ex)
                .ConfigureAwait(false);
        }
    }

    private static async Task<Exception>
        CreatePreservedRecoveryExceptionAsync(
            RepositoryInfo repository,
            IGitCliRunner runner,
            PendingPullRecovery recovery,
            Exception failure)
    {
        try
        {
            string recoveryBranchName = await PreserveCheckpointOnRecoveryBranchAsync(
                    repository,
                    runner,
                    recovery,
                    CancellationToken.None)
                .ConfigureAwait(false);
            return new PendingPullRecoveryPreservedException(
                recoveryBranchName,
                failure);
        }
        catch (Exception preservationFailure)
        {
            return await CreateCheckpointPreservationExceptionAsync(
                    repository,
                    runner,
                    recovery,
                    new AggregateException(
                    "The pending pull recovery failed and its durable recovery branch could not be published.",
                    failure,
                    preservationFailure))
                .ConfigureAwait(false);
        }
    }

    private static async Task<Exception> CreateCheckpointPreservationExceptionAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        PendingPullRecovery recovery,
        Exception failure)
    {
        try
        {
            string? checkpointCommit = await TryResolveCommitAsync(
                    repository,
                    runner,
                    recovery.Checkpoint.RefName,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (string.Equals(
                    checkpointCommit,
                    recovery.Checkpoint.Commit,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new PendingPullRecoveryPreservedException(
                    recovery.Checkpoint.RefName,
                    failure);
            }

            return new AggregateException(
                "The pending pull recovery failed and its checkpoint reference no longer identifies the expected commit.",
                failure);
        }
        catch (Exception verificationFailure)
        {
            return new AggregateException(
                "The pending pull recovery failed and its checkpoint reference could not be verified.",
                failure,
                verificationFailure);
        }
    }

    private static async Task<string> PreserveCheckpointOnRecoveryBranchAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        PendingPullRecovery recovery,
        CancellationToken cancellationToken)
    {
        string branchName = recovery.RecoveryBranchName;
        string? existing = await TryResolveCommitAsync(
                repository,
                runner,
                $"refs/heads/{branchName}",
                cancellationToken)
            .ConfigureAwait(false);
        if (string.Equals(
                existing,
                recovery.Checkpoint.Commit,
                StringComparison.OrdinalIgnoreCase))
        {
            return branchName;
        }

        if (existing is not null)
        {
            throw new InvalidOperationException(
                $"The recovery branch '{branchName}' already identifies another commit.");
        }

        // A branch such as beutl takes the path the usual name needs, so Git refuses to create it and the
        // checkpoint goes on a later name. A later name that already holds it is an earlier attempt's.
        IReadOnlyList<string> candidates = recovery.RecoveryBranchNameCandidates;
        var occupied = new HashSet<string>(StringComparer.Ordinal);
        foreach (string candidate in candidates.Skip(1))
        {
            string? preserved = await TryResolveCommitAsync(
                    repository,
                    runner,
                    $"refs/heads/{candidate}",
                    cancellationToken)
                .ConfigureAwait(false);
            if (string.Equals(
                    preserved,
                    recovery.Checkpoint.Commit,
                    StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            if (preserved is not null)
            {
                occupied.Add(candidate);
            }
        }

        var publicationFailures = new List<Exception>();
        foreach (string candidate in candidates)
        {
            if (occupied.Contains(candidate))
            {
                continue;
            }

            string branchRef = $"refs/heads/{candidate}";
            try
            {
                await runner.RunAsync(
                    repository,
                    [
                        "update-ref",
                        "--create-reflog",
                        "-m",
                        "beutl preserve pending pull checkpoint",
                        branchRef,
                        recovery.Checkpoint.Commit,
                        string.Empty,
                    ],
                    GitCommandOptions.Local,
                    cancellationToken).ConfigureAwait(false);
                return candidate;
            }
            catch (Exception publicationException)
            {
                string? published = await TryResolveCommitAsync(
                        repository,
                        runner,
                        branchRef,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (string.Equals(
                        published,
                        recovery.Checkpoint.Commit,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }

                publicationFailures.Add(publicationException);
            }
        }

        throw new AggregateException(
            $"The recovery branch '{branchName}' could not be published safely.",
            publicationFailures);
    }

    private async Task<bool> TryReapplyCheckpointToExternallyOwnedTipAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        PendingPullRecovery recovery,
        CheckedOutBranchTip actualTip,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(
                actualTip.RefName,
                recovery.Checkpoint.BaseTip.RefName,
                StringComparison.Ordinal))
        {
            return false;
        }

        // Validate before any temporary commit or checkout can replace a symlinked project path.
        ValidateRecoveryProjectFilePhysicalContainment(
            repository,
            recovery.ProjectFile);

        string desiredTree = await BuildProjectTreeAsync(
                repository,
                runner,
                actualTip.Commit,
                recovery.Checkpoint.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        WorktreeStateFingerprint actualState = await CaptureWorktreeStateAsync(
                repository,
                runner,
                actualTip.Commit,
                repository.Pathspec,
                cancellationToken)
            .ConfigureAwait(false);
        string actualTree = await ResolveTreeAsync(
                repository,
                runner,
                actualTip.Commit,
                cancellationToken)
            .ConfigureAwait(false);
        bool indexAtActual = await IsIndexAtCommitAsync(
                repository,
                runner,
                actualTip.Commit,
                repository.Pathspec,
                cancellationToken)
            .ConfigureAwait(false);

        if (string.Equals(actualState.Tree, desiredTree, StringComparison.OrdinalIgnoreCase))
        {
            bool indexAtBase = indexAtActual || await IsIndexAtCommitAsync(
                    repository,
                    runner,
                    recovery.Checkpoint.BaseTip.Commit,
                    repository.Pathspec,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!indexAtBase)
            {
                return false;
            }

            CheckedOutBranchTip beforeReset = await GetCheckedOutBranchTipCoreAsync(
                    repository,
                    runner,
                    cancellationToken)
                .ConfigureAwait(false);
            if (!EqualsBranchTip(beforeReset, actualTip))
            {
                return false;
            }

            string originalIndexCommit = indexAtActual
                ? actualTip.Commit
                : recovery.Checkpoint.BaseTip.Commit;
            try
            {
                if (!indexAtActual)
                {
                    await ResetIndexAsync(
                            repository,
                            runner,
                            actualTip.Commit,
                            repository.Pathspec)
                        .ConfigureAwait(false);
                }

                CheckedOutBranchTip verifiedTip = await GetCheckedOutBranchTipCoreAsync(
                        repository,
                        runner,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                WorktreeStateFingerprint verifiedState = await CaptureWorktreeStateAsync(
                        repository,
                        runner,
                        actualTip.Commit,
                        repository.Pathspec,
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (!EqualsBranchTip(verifiedTip, actualTip)
                    || !string.Equals(
                        verifiedState.Tree,
                        desiredTree,
                        StringComparison.OrdinalIgnoreCase)
                    || !await IsIndexAtCommitAsync(
                            repository,
                            runner,
                            actualTip.Commit,
                            repository.Pathspec,
                            CancellationToken.None)
                        .ConfigureAwait(false))
                {
                    throw new ProjectCheckpointStateChangedException();
                }

                return true;
            }
            catch (Exception ex)
            {
                Exception failure = ex;
                if (!indexAtActual)
                {
                    try
                    {
                        await ResetIndexAsync(
                                repository,
                                runner,
                            originalIndexCommit,
                            repository.Pathspec)
                            .ConfigureAwait(false);
                    }
                    catch (Exception restoreException)
                    {
                        failure = new AggregateException(
                            "The checkpoint index reapply failed and the prior index could not be restored.",
                            ex,
                            restoreException);
                    }
                }

                throw failure;
            }
        }

        if (!string.Equals(actualState.Tree, actualTree, StringComparison.OrdinalIgnoreCase)
            || !indexAtActual)
        {
            return false;
        }

        string targetCommit = await CreateTreeCommitAsync(
                repository,
                runner,
                desiredTree,
                actualTip.Commit,
                "beutl temporary pending pull recovery",
                cancellationToken)
            .ConfigureAwait(false);

        TreeTransitionResult transition = await ApplyTreeTransitionAsync(
                repository,
                runner,
                actualTip,
                actualTip,
                actualTip.Commit,
                targetCommit,
                "beutl reapply pending pull checkpoint",
                new TreeTransitionIndexPlan(
                    FinalCommit: actualTip.Commit,
                    RestoreCommit: actualTip.Commit,
                    Pathspec: repository.Pathspec),
                cancellationToken,
                validatePreparedTarget: () =>
                    ValidateRecoveryProjectFilePhysicalContainment(
                        repository,
                        recovery.ProjectFile))
            .ConfigureAwait(false);
        return transition.Outcome switch
        {
            TreeTransitionOutcome.AppliedTarget => true,
            TreeTransitionOutcome.OwnershipLost => false,
            _ => throw new InvalidOperationException(
                "The pending pull checkpoint could not be reapplied safely.",
                transition.Error),
        };
    }

    private static async Task<string> CreateTreeCommitAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        string tree,
        string parentCommit,
        string message,
        CancellationToken cancellationToken)
    {
        GitCommandResult result = await runner.RunAsync(
                repository,
                ["commit-tree", tree, "-p", parentCommit, "-m", message],
                new GitCommandOptions(
                    GitCommandExecutionKind.Local,
                    EnvironmentOverrides: new Dictionary<string, string?>
                    {
                        ["GIT_AUTHOR_NAME"] = "Beutl Recovery",
                        ["GIT_AUTHOR_EMAIL"] = "beutl-recovery@localhost",
                        ["GIT_COMMITTER_NAME"] = "Beutl Recovery",
                        ["GIT_COMMITTER_EMAIL"] = "beutl-recovery@localhost",
                    }),
                cancellationToken)
            .ConfigureAwait(false);
        string commit = result.Stdout.Trim();
        if (commit.Length == 0)
        {
            throw new InvalidOperationException("Git did not return the temporary recovery commit.");
        }

        await runner.RunAsync(
                repository,
                ["cat-file", "-e", commit + "^{commit}"],
                GitCommandOptions.Local,
                cancellationToken)
            .ConfigureAwait(false);

        return commit;
    }

    private async Task CompletePendingPullRecoveryCoreAsync(
        PendingPullRecovery recovery,
        CancellationToken cancellationToken)
    {
        RepositoryInfo repository = GetRepository();
        IGitCliRunner runner = await GetInstalledRunnerCoreAsync(cancellationToken)
            .ConfigureAwait(false);
        await ValidatePendingPullRecoveryAsync(
                repository,
                runner,
                recovery,
                cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        string commands = string.Join(
            '\n',
            "start",
            $"delete {recovery.DescriptorRef} {recovery.DescriptorObject}",
            $"delete {recovery.Checkpoint.RefName} {recovery.Checkpoint.Commit}",
            "prepare",
            "commit",
            string.Empty);
        try
        {
            await runner.RunAsync(
                repository,
                ["update-ref", "--stdin"],
                new GitCommandOptions(
                    GitCommandExecutionKind.Local,
                    StandardInput: commands),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            string? remainingDescriptor = await TryResolveObjectAsync(
                    repository,
                    runner,
                    recovery.DescriptorRef,
                    CancellationToken.None)
                .ConfigureAwait(false);
            string? remainingCheckpoint = await TryResolveCommitAsync(
                    repository,
                    runner,
                    recovery.Checkpoint.RefName,
                    CancellationToken.None)
                .ConfigureAwait(false);
            if (remainingDescriptor is null && remainingCheckpoint is null)
            {
                return;
            }

            throw new PendingPullRecoveryChangedException(recovery.DescriptorRef, ex);
        }
    }

    private static async Task ValidatePendingPullRecoveryAsync(
        RepositoryInfo repository,
        IGitCliRunner runner,
        PendingPullRecovery recovery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recovery);
        string expectedRef = GetPendingRecoveryRefPrefix(repository) + recovery.Id;
        if (!Guid.TryParseExact(recovery.Id, "N", out _)
            || !string.Equals(recovery.DescriptorRef, expectedRef, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The pending pull recovery does not belong to this project.",
                nameof(recovery));
        }

        GitRevisionValidator.ValidateCommitId(
            recovery.DescriptorObject,
            nameof(recovery));
        ValidateCheckpointRef(repository, recovery.Checkpoint);
        ValidateAttachedBranchTip(recovery.TargetTip, nameof(recovery));
        if (!string.Equals(
                recovery.Checkpoint.BaseTip.RefName,
                recovery.TargetTip.RefName,
                StringComparison.Ordinal)
            || recovery.CreatedAt == default)
        {
            throw new ArgumentException(
                "The pending pull recovery descriptor is inconsistent.",
                nameof(recovery));
        }

        _ = ValidateStoredRecoveryProjectFile(repository, recovery.ProjectFile);
        string? currentObject = await TryResolveObjectAsync(
                repository,
                runner,
                recovery.DescriptorRef,
                cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(
                currentObject,
                recovery.DescriptorObject,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new PendingPullRecoveryChangedException(recovery.DescriptorRef);
        }

        await ValidateCheckpointAsync(repository, runner, recovery.Checkpoint, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string GetRecoveryProjectFile(
        RepositoryInfo repository,
        string projectFile)
    {
        string projectRoot = RepositoryPathComparer.ResolveCanonicalPath(repository.ProjectRoot);
        string fullPath = RepositoryPathComparer.ResolveCanonicalPath(projectFile);
        string canonicalRelativePath = Path.GetRelativePath(projectRoot, fullPath);
        ValidateRecoveryProjectFileContainment(canonicalRelativePath);

        return GetRecoveryProjectFileLexically(
            repository,
            projectFile,
            canonicalRelativePath);
    }

    private static string GetRecoveryProjectFileLexically(
        RepositoryInfo repository,
        string projectFile,
        string? canonicalRelativePath = null)
    {

        string lexicalProjectFile = Path.GetFullPath(projectFile);
        string? lexicalRoot = Path.GetDirectoryName(lexicalProjectFile);
        while (lexicalRoot is not null)
        {
            if (RepositoryPathComparer.AreEquivalent(lexicalRoot, repository.ProjectRoot))
            {
                string lexicalRelativePath = Path.GetRelativePath(
                    lexicalRoot,
                    lexicalProjectFile);
                ValidateRecoveryProjectFile(lexicalRelativePath);
                return NormalizeGitPath(lexicalRelativePath);
            }

            lexicalRoot = Path.GetDirectoryName(lexicalRoot);
        }

        canonicalRelativePath ??= Path.GetRelativePath(
            Path.GetFullPath(repository.ProjectRoot),
            lexicalProjectFile);
        ValidateRecoveryProjectFile(canonicalRelativePath);
        return NormalizeGitPath(canonicalRelativePath);
    }

    private static string GetLexicalRecoveryProjectFile(
        RepositoryInfo repository,
        string relativeProjectFile)
    {
        ValidateRecoveryProjectFile(relativeProjectFile);
        return Path.GetFullPath(Path.Combine(
            repository.ProjectRoot,
            relativeProjectFile.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static string ValidateStoredRecoveryProjectFile(
        RepositoryInfo repository,
        string projectFile)
    {
        string relativeProjectFile = Path.GetRelativePath(
            Path.GetFullPath(repository.ProjectRoot),
            Path.GetFullPath(projectFile));
        ValidateRecoveryProjectFile(relativeProjectFile);
        return NormalizeGitPath(relativeProjectFile);
    }

    private static void ValidateRecoveryProjectFilePhysicalContainment(
        RepositoryInfo repository,
        string projectFile)
    {
        if (!RepositoryPathComparer.IsContainedWithin(repository.ProjectRoot, projectFile))
        {
            throw new ArgumentException(
                $"The pending pull recovery project file '{projectFile}' must remain inside the project root.",
                nameof(projectFile));
        }
    }

    private static void ValidateRecoveryProjectFile(string relativePath)
    {
        ValidateRecoveryProjectFileContainment(relativePath);
        if (!string.Equals(
                Path.GetExtension(relativePath),
                ".bep",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"The pending pull recovery project file '{relativePath}' must use the .bep extension.",
                nameof(relativePath));
        }
    }

    private static void ValidateRecoveryProjectFileContainment(string relativePath)
    {
        char[] separators = OperatingSystem.IsWindows() ? ['/', '\\'] : ['/'];
        if (string.IsNullOrWhiteSpace(relativePath)
            || relativePath == ".."
            || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            || relativePath.StartsWith("../", StringComparison.Ordinal)
            || Path.IsPathRooted(relativePath)
            || relativePath
                .Split(separators, StringSplitOptions.RemoveEmptyEntries)
                .Any(static component => component is "." or ".."))
        {
            throw new ArgumentException(
                $"The pending pull recovery project file '{relativePath}' must remain inside the project root.",
                nameof(relativePath));
        }
    }
}
