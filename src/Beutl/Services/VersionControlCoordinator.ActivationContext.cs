using Beutl.Editor.VersionControl;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    private sealed class ActivationContext
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cancellation;
        private readonly TaskCompletionSource _cancellationQuiesced = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly HashSet<IProjectVersionControlBackend> _cleanupDelegatedServices = new(
            ReferenceEqualityComparer.Instance);
        private Task _completionDependency = Task.CompletedTask;
        private IProjectVersionControlBackend _ownedService;
        private int _activeCancellations;
        private bool _cleanupStarted;
        private bool _completionRequested;
        private bool _hasPredecessors;

        public ActivationContext(
            long revision,
            string projectRoot,
            string projectFile,
            IProjectVersionControlBackend service,
            bool isNewProject = false,
            CancellationToken cancellationToken = default)
        {
            Revision = revision;
            ProjectRoot = projectRoot;
            ProjectFile = projectFile;
            Service = service;
            IsNewProject = isNewProject;
            _ownedService = service;
            _cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        public long Revision { get; }

        public string ProjectRoot { get; }

        public string ProjectFile { get; }

        public IProjectVersionControlBackend Service { get; }

        // Set for a project the app has just created, whose tracking the new-project dialog decides.
        public bool IsNewProject { get; }

        // For a new project, the repository its initialization created or attached before it stopped.
        public RepositoryInfo? InterruptedNewProjectRepository { get; init; }

        public CancellationToken CancellationToken => _cancellation.Token;

        public Task CancellationQuiesced => _cancellationQuiesced.Task;

        public Task Completion => _completion.Task;

        public bool HasPredecessors
        {
            get
            {
                lock (_gate)
                {
                    return _hasPredecessors;
                }
            }
        }

        public Task PredecessorsCompleted
        {
            get
            {
                lock (_gate)
                {
                    return _completionDependency;
                }
            }
        }

        public bool OwnsService(IProjectVersionControlBackend service)
        {
            lock (_gate)
            {
                return ReferenceEquals(_ownedService, service);
            }
        }

        public void TransferOwnership(IProjectVersionControlBackend service)
        {
            lock (_gate)
            {
                _ownedService = service;
            }
        }

        public void AddCompletionDependency(Task completion)
        {
            lock (_gate)
            {
                _hasPredecessors = true;
                _completionDependency = Task.WhenAll(_completionDependency, completion);
            }
        }

        public void Cancel()
        {
            lock (_gate)
            {
                if (_cleanupStarted)
                {
                    return;
                }

                _activeCancellations++;
            }

            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                bool cleanup;
                lock (_gate)
                {
                    _activeCancellations--;
                    cleanup = TryBeginCleanupLocked();
                }

                if (cleanup)
                {
                    FinishCleanup();
                }
            }
        }

        public void Complete()
        {
            bool cleanup;
            lock (_gate)
            {
                _completionRequested = true;
                cleanup = TryBeginCleanupLocked();
            }

            if (cleanup)
            {
                FinishCleanup();
            }
        }

        public bool IsServiceCleanupDelegated(IProjectVersionControlBackend service)
        {
            lock (_gate)
            {
                return _cleanupDelegatedServices.Contains(service);
            }
        }

        public void MarkServiceCleanupDelegated(IProjectVersionControlBackend service)
        {
            lock (_gate)
            {
                _cleanupDelegatedServices.Add(service);
            }
        }

        public void Finish()
        {
            _completion.TrySetResult();
        }

        private bool TryBeginCleanupLocked()
        {
            if (_cleanupStarted || !_completionRequested || _activeCancellations != 0)
            {
                return false;
            }

            _cleanupStarted = true;
            return true;
        }

        private void FinishCleanup()
        {
            try
            {
                _cancellation.Dispose();
            }
            finally
            {
                _cancellationQuiesced.TrySetResult();
            }
        }
    }
}
