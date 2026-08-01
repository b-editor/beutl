using Reactive.Bindings;

namespace Beutl.Editor.VersionControl;

public interface IProjectVersionControlSession
{
    IReadOnlyReactiveProperty<bool> IsGitAvailable { get; }

    IReadOnlyReactiveProperty<bool> IsTracked { get; }

    Task NotifySavedAsync(CancellationToken cancellationToken = default);
}
