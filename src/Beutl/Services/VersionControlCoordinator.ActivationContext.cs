using Beutl.Editor.VersionControl;

namespace Beutl.Services;

internal partial class VersionControlCoordinator
{
    // Decides how the open project is tracked: it looks for the project's repository with the untracked
    // backend shown meanwhile, and adopts a tracked backend once the user agrees where that is needed.
    private sealed class ActivationContext
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly TaskCompletionSource _completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _finished;

        public ActivationContext(
            string projectRoot,
            string projectFile,
            IProjectVersionControlBackend service,
            bool isNewProject,
            CancellationTokenSource cancellation)
        {
            ProjectRoot = projectRoot;
            ProjectFile = projectFile;
            Service = service;
            IsNewProject = isNewProject;
            _cancellation = cancellation;
            CancellationToken = cancellation.Token;
        }

        public string ProjectRoot { get; }

        public string ProjectFile { get; }

        // The untracked backend the activation discovers with, which the project shows meanwhile.
        public IProjectVersionControlBackend Service { get; }

        // The tracked backend the activation prepares for the repository it found.
        public IProjectVersionControlBackend? Candidate { get; set; }

        // Set for a project the app has just created, whose tracking the new-project dialog decides.
        public bool IsNewProject { get; }

        // Set when a changed Git executable started the activation, which a newer change restarts.
        public bool IsRediscovery { get; init; }

        // For a new project, the repository its initialization created or attached before it stopped.
        public RepositoryInfo? InterruptedNewProjectRepository { get; init; }

        public CancellationToken CancellationToken { get; }

        public Task Completion => _completion.Task;

        public bool Uses(IProjectVersionControlBackend service)
        {
            return !_finished
                   && (ReferenceEquals(Service, service) || ReferenceEquals(Candidate, service));
        }

        public void Cancel()
        {
            if (!_finished)
            {
                _cancellation.Cancel();
            }
        }

        public void Finish()
        {
            _finished = true;
            _cancellation.Dispose();
            _completion.TrySetResult();
        }
    }
}
