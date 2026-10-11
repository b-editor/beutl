using Reactive.Bindings;

namespace Beutl.Editor.VersionControl;

internal interface IProjectVersionControlSession
{
    IReadOnlyReactiveProperty<bool> IsGitAvailable { get; }

    IReadOnlyReactiveProperty<bool> IsTracked { get; }

    /// <summary>
    /// Records that the project was explicitly saved, so a Save snapshot can be committed. It never
    /// waits for another version-control operation, such as a push: the snapshot then runs once that
    /// operation has finished and records the project files as they are by then.
    /// </summary>
    /// <param name="completedWrite">
    /// The reservation the finished save held. Passing it lets the snapshot take over the
    /// workspace without ever leaving it unreserved, so the caller must have finished writing.
    /// The snapshot may decline it — it is skipped entirely when the repository is untracked or
    /// automatic snapshots are off, and deferred while another operation runs — so the caller still
    /// owns the reservation and must dispose it; disposing one that was taken over is a no-op.
    /// Passing <see langword="null"/> makes the snapshot compete for the workspace and be skipped
    /// when another operation holds it.
    /// </param>
    Task NotifySavedAsync(
        IProjectFileWriteLease? completedWrite = null,
        CancellationToken cancellationToken = default);
}
