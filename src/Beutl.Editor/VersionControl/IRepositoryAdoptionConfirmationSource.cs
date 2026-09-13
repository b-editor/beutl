namespace Beutl.Editor.VersionControl;

internal interface IRepositoryAdoptionConfirmationSource
{
    event EventHandler? RepositoryAdoptionChanged;

    RepositoryAdoptionRequest? PendingRepositoryAdoption { get; }
}

internal sealed class RepositoryAdoptionRequest(RepositoryInfo repository)
{
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public RepositoryInfo Repository { get; } = repository;

    internal Task<bool> Completion => _completion.Task;

    public bool Respond(bool accepted) => _completion.TrySetResult(accepted);

    internal void Cancel(CancellationToken cancellationToken)
        => _completion.TrySetCanceled(cancellationToken);
}
