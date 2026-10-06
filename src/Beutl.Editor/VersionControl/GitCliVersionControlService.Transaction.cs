namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private sealed class Transaction : IProjectVersionControlTransaction
    {
        private readonly GitCliVersionControlService _service;

        public Transaction(GitCliVersionControlService service)
        {
            _service = service;
        }

        public Task<CommitResult> CommitAllAsync(
            string message,
            SnapshotKind kind,
            CancellationToken cancellationToken)
            => _service.CommitAllCoreAsync(message, kind, cancellationToken);

        public Task<CheckedOutBranchTip> GetCheckedOutBranchTipAsync(
            CancellationToken cancellationToken)
            => _service.GetCheckedOutBranchTipCoreAsync(cancellationToken);

        public Task<PullPreflightResult> PreflightPullAsync(
            CheckedOutBranchTip expectedCurrent,
            CancellationToken cancellationToken)
            => _service.PreflightPullCoreAsync(expectedCurrent, cancellationToken);

        public Task<ProjectCheckpoint> CreateProjectCheckpointAsync(
            string message,
            CancellationToken cancellationToken)
            => _service.CreateProjectCheckpointCoreAsync(message, cancellationToken);

        public Task<PendingPullRecovery> PersistPendingPullRecoveryAsync(
            ProjectCheckpoint checkpoint,
            CheckedOutBranchTip targetTip,
            string projectFile,
            CancellationToken cancellationToken)
            => _service.PersistPendingPullRecoveryCoreAsync(
                checkpoint,
                targetTip,
                projectFile,
                cancellationToken);

        public Task<IReadOnlyList<PendingPullRecovery>> GetPendingPullRecoveriesAsync(
            CancellationToken cancellationToken)
            => _service.GetPendingPullRecoveriesCoreAsync(cancellationToken);

        public Task<PendingPullRecoveryOutcome> RecoverPendingPullRecoveryAsync(
            PendingPullRecovery recovery,
            CancellationToken cancellationToken)
            => _service.RecoverPendingPullRecoveryCoreAsync(recovery, cancellationToken);

        public Task CompletePendingPullRecoveryAsync(
            PendingPullRecovery recovery,
            CancellationToken cancellationToken)
            => _service.CompletePendingPullRecoveryCoreAsync(recovery, cancellationToken);

        public Task RestoreProjectCheckpointAsync(
            ProjectCheckpoint checkpoint,
            CancellationToken cancellationToken)
            => _service.RestoreProjectCheckpointCoreAsync(checkpoint, cancellationToken);

        public Task<CommitResult> CommitProjectTreeAsync(
            CheckedOutBranchTip expectedCurrent,
            string sourceCommit,
            string message,
            SnapshotKind kind,
            CancellationToken cancellationToken)
            => _service.CommitProjectTreeCoreAsync(
                expectedCurrent,
                sourceCommit,
                message,
                kind,
                cancellationToken);

        public Task<bool> RevisionContainsProjectFileAsync(
            string sha,
            string projectFile,
            CancellationToken cancellationToken)
            => _service.RevisionContainsProjectFileCoreAsync(
                sha,
                projectFile,
                cancellationToken);

        public Task<BranchTipRollbackResult> TryRollbackBranchTipAsync(
            CheckedOutBranchTip expectedCurrent,
            CheckedOutBranchTip target,
            CancellationToken cancellationToken)
            => _service.TryRollbackBranchTipCoreAsync(expectedCurrent, target, cancellationToken);

        public Task<bool> DeleteProjectCheckpointAsync(
            ProjectCheckpoint checkpoint,
            CancellationToken cancellationToken)
            => _service.DeleteProjectCheckpointCoreAsync(checkpoint, cancellationToken);

        public Task<WorkspaceStatus> GetStatusAsync(CancellationToken cancellationToken)
            => _service.GetStatusCoreAsync(cancellationToken);

        public Task<IReadOnlyList<BranchInfo>> GetBranchesAsync(
            CancellationToken cancellationToken)
            => _service.GetBranchesCoreAsync(cancellationToken);

        public Task<bool> CanCreateBranchAsync(
            string name,
            CancellationToken cancellationToken)
            => _service.CanCreateBranchCoreAsync(name, cancellationToken);

        public Task CreateBranchAsync(
            string name,
            string startPoint,
            CancellationToken cancellationToken)
            => _service.CreateBranchCoreAsync(name, startPoint, cancellationToken);

        public Task PrefetchBranchLfsObjectsAsync(string name, CancellationToken cancellationToken)
            => _service.PrefetchBranchLfsObjectsCoreAsync(name, cancellationToken);

        public Task PrefetchCommitLfsObjectsAsync(
            string sha,
            LfsPrefetchScope scope,
            CancellationToken cancellationToken)
            => _service.PrefetchCommitLfsObjectsCoreAsync(sha, scope, cancellationToken);

        public Task SwitchBranchAsync(string name, CancellationToken cancellationToken)
            => _service.SwitchBranchCoreAsync(name, cancellationToken);

        public Task<FastForwardPullResult> PullFastForwardAsync(
            CheckedOutBranchTip expectedCurrent,
            ProjectCheckpoint? checkpoint,
            string projectFile,
            CancellationToken cancellationToken)
            => _service.PullFastForwardCoreAsync(
                expectedCurrent,
                checkpoint,
                projectFile,
                cancellationToken);
    }
}
