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

        public Task<CommitResult> RestoreProjectTreeAsync(
            string sourceCommit,
            string message,
            SnapshotKind kind,
            CancellationToken cancellationToken)
            => _service.RestoreProjectTreeCoreAsync(
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

        public Task<RemoteOpResult> PullFastForwardAsync(
            string upstreamCommit,
            CancellationToken cancellationToken)
            => _service.PullFastForwardCoreAsync(upstreamCommit, cancellationToken);
    }
}
