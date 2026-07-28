using Beutl.Editor.VersionControl;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public sealed class GitIdentityDialogViewModel
{
    private readonly IProjectVersionControlService _versionControlService;

    public GitIdentityDialogViewModel(IProjectVersionControlService versionControlService)
    {
        _versionControlService = versionControlService
                                 ?? throw new ArgumentNullException(nameof(versionControlService));
        Name.Value = Environment.UserName;
        CanSave = Name.CombineLatest(
                Email,
                static (name, email) =>
                    !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(email))
            .ToReadOnlyReactivePropertySlim();
    }

    public ReactivePropertySlim<string> Name { get; } = new();

    public ReactivePropertySlim<string> Email { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> CanSave { get; }

    public Task SaveAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSave.Value)
        {
            throw new InvalidOperationException("A Git user name and email address are required.");
        }

        return _versionControlService.SetLocalIdentityAsync(
            new GitIdentity(Name.Value.Trim(), Email.Value.Trim()),
            cancellationToken);
    }
}
