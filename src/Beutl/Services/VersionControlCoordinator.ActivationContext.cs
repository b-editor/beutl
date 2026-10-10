using Beutl.Editor.VersionControl;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    // Used only on the UI thread, like the rest of the coordinator's state.
    private sealed class ActivationContext
    {
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

        public bool HasPredecessors { get; private set; }

        public Task PredecessorsCompleted => _completionDependency;

        public bool OwnsService(IProjectVersionControlBackend service)
        {
            return ReferenceEquals(_ownedService, service);
        }

        public void TransferOwnership(IProjectVersionControlBackend service)
        {
            _ownedService = service;
        }

        public void AddCompletionDependency(Task completion)
        {
            HasPredecessors = true;
            _completionDependency = Task.WhenAll(_completionDependency, completion);
        }

        // A cancellation callback can run activation code inline, including Complete, so the source is
        // disposed only once no Cancel is still running its callbacks.
        public void Cancel()
        {
            if (_cleanupStarted)
            {
                return;
            }

            _activeCancellations++;
            try
            {
                _cancellation.Cancel();
            }
            finally
            {
                _activeCancellations--;
                TryCleanup();
            }
        }

        public void Complete()
        {
            _completionRequested = true;
            TryCleanup();
        }

        public bool IsServiceCleanupDelegated(IProjectVersionControlBackend service)
        {
            return _cleanupDelegatedServices.Contains(service);
        }

        public void MarkServiceCleanupDelegated(IProjectVersionControlBackend service)
        {
            _cleanupDelegatedServices.Add(service);
        }

        public void Finish()
        {
            _completion.TrySetResult();
        }

        private void TryCleanup()
        {
            if (_cleanupStarted || !_completionRequested || _activeCancellations != 0)
            {
                return;
            }

            _cleanupStarted = true;
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
