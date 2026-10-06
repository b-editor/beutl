using System.Text;

namespace Beutl.Editor.VersionControl;

internal sealed partial class GitCliVersionControlService
{
    private sealed class HeadOwnershipLease : IDisposable
    {
        // The reftable backend keeps HEAD in its tables and leaves this placeholder in the HEAD file,
        // so only Git can say which branch such a worktree has checked out. HEAD.lock does not stop
        // that backend from moving HEAD either, so there the check before each ref update is the guard.
        private const string ReftableHeadPlaceholder = "ref: refs/heads/.invalid\n";
        private readonly Action<Exception>? _releaseFailureSink;
        private readonly RepositoryInfo _repository;
        private readonly IGitCliRunner _runner;
        private readonly string _headPath;
        private readonly string _expectedRefName;
        private FileStream? _stream;

        private HeadOwnershipLease(
            RepositoryInfo repository,
            IGitCliRunner runner,
            string headPath,
            string expectedRefName,
            string lockPath,
            FileStream stream,
            Action<Exception>? releaseFailureSink)
        {
            _repository = repository;
            _runner = runner;
            _headPath = headPath;
            _expectedRefName = expectedRefName;
            LockPath = lockPath;
            _stream = stream;
            _releaseFailureSink = releaseFailureSink;
        }

        public string LockPath { get; }

        // Set once the HEAD file has shown the reftable placeholder. HEAD.lock then guards nothing, so a
        // branch update has to let Git verify the checked-out branch inside its own transaction.
        public bool HeadStoredInReftable { get; private set; }

        public static async Task<HeadOwnershipLease> AcquireAsync(
            RepositoryInfo repository,
            IGitCliRunner runner,
            string headPath,
            string expectedRefName,
            Action<Exception>? releaseFailureSink,
            CancellationToken cancellationToken)
        {
            string lockPath = headPath + ".lock";
            FileStream stream;
            try
            {
                stream = new FileStream(
                    lockPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.WriteThrough);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new GitOperationException(
                    128,
                    $"Unable to acquire the worktree HEAD lock '{lockPath}': {ex.Message}");
            }

            var lease = new HeadOwnershipLease(
                repository,
                runner,
                headPath,
                expectedRefName,
                lockPath,
                stream,
                releaseFailureSink);
            try
            {
                await lease.VerifyStillOwnedAsync(cancellationToken).ConfigureAwait(false);
                return lease;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }

        public async Task VerifyStillOwnedAsync(CancellationToken cancellationToken)
        {
            if (_stream is null)
            {
                throw new ObjectDisposedException(nameof(HeadOwnershipLease));
            }

            string actual = File.ReadAllText(_headPath, new UTF8Encoding(false));
            if (string.Equals(actual, $"ref: {_expectedRefName}\n", StringComparison.Ordinal))
            {
                return;
            }

            if (!string.Equals(actual, ReftableHeadPlaceholder, StringComparison.Ordinal))
            {
                throw new ProjectCheckpointStateChangedException();
            }

            HeadStoredInReftable = true;
            GitCommandResult symbolicRef;
            try
            {
                symbolicRef = await _runner.RunAsync(
                    _repository,
                    ["symbolic-ref", "--quiet", "HEAD"],
                    GitCommandOptions.Local,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (GitOperationException ex) when (ex.ExitCode == 1)
            {
                throw new ProjectCheckpointStateChangedException();
            }

            if (!string.Equals(symbolicRef.Stdout.Trim(), _expectedRefName, StringComparison.Ordinal))
            {
                throw new ProjectCheckpointStateChangedException();
            }
        }

        public void Dispose()
        {
            FileStream? stream = Interlocked.Exchange(ref _stream, null);
            if (stream is null)
            {
                return;
            }

            try
            {
                stream.Dispose();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _releaseFailureSink?.Invoke(ex);
            }

            try
            {
                File.Delete(LockPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _releaseFailureSink?.Invoke(ex);
            }
        }
    }
}
