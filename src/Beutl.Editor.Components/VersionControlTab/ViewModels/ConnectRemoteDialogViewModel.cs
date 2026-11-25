using System.Reactive.Disposables;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Services;
using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Editor.VersionControl;
using Beutl.Language;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

internal sealed record ConnectRemoteContext(string ProjectName, bool HasOrigin, string CurrentUrl, bool Publish);

internal sealed class ConnectRemoteDialogViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly CancellationTokenSource _lifetime;
    private readonly Func<GitRemoteUrl, CancellationToken, Task> _apply;
    private readonly Dictionary<(string Owner, string Name), Guid> _creationIds;
    private string? _creationOwnerId;
    private bool _disposed;

    public ConnectRemoteDialogViewModel(
        BeutlApiApplication? api, ConnectRemoteContext context,
        Dictionary<(string Owner, string Name), Guid> creationIds,
        Func<GitRemoteUrl, CancellationToken, Task> apply, CancellationToken cancellationToken)
    {
        Context = context;
        _creationIds = creationIds;
        _apply = apply;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Picker = new HostedRepositoryPickerViewModel(api, _lifetime.Token).DisposeWith(_disposables);
        Mode = new ReactivePropertySlim<int>()
            .DisposeWith(_disposables);
        Name = new ReactivePropertySlim<string>(context.ProjectName).DisposeWith(_disposables);
        Url = new ReactivePropertySlim<string>(context.CurrentUrl).DisposeWith(_disposables);
        IsBusy = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        Error = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        IsNameValid = Name.Select(HostedGitRepositoryService.IsValidRepositoryName)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
        CanConnect = Mode.CombineLatest(IsNameValid, Url, Picker.NeedsSignIn, Picker.SelectedRepository, IsBusy,
                static (mode, validName, url, needsSignIn, selected, busy) => !busy && mode switch
                {
                    0 => !needsSignIn && validName,
                    1 => !needsSignIn && selected is not null,
                    _ => !string.IsNullOrWhiteSpace(url),
                })
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
        ButtonText = Mode.Select(mode => (mode, Context.Publish) switch
            {
                (0, true) => Strings.HostedGit_CreateAndPublish,
                (0, false) => Strings.HostedGit_CreateAndConnect,
                (_, true) => Strings.HostedGit_ConnectAndPublish,
                _ => Strings.HostedGit_Connect,
            })
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);
        Mode.Subscribe(mode =>
        {
            Error.Value = string.Empty;
            Picker.SetActive(mode == 1);
        }).DisposeWith(_disposables);
    }

    public ConnectRemoteContext Context { get; }
    public HostedRepositoryPickerViewModel Picker { get; }
    public ReactivePropertySlim<string> Name { get; }
    public ReactivePropertySlim<string> Url { get; }
    public ReactivePropertySlim<int> Mode { get; }
    public ReactivePropertySlim<bool> IsBusy { get; }
    public ReactivePropertySlim<string> Error { get; }
    public ReadOnlyReactivePropertySlim<string?> ButtonText { get; }
    public ReadOnlyReactivePropertySlim<bool> CanConnect { get; }
    public ReadOnlyReactivePropertySlim<bool> IsNameValid { get; }

    public bool IsCanceled => _lifetime.IsCancellationRequested;

    public void Cancel() => _lifetime.Cancel();

    public async Task<bool> ConnectAsync()
    {
        if (_disposed || !CanConnect.Value)
            return false;

        IsBusy.Value = true;
        Error.Value = string.Empty;
        CancellationToken token = _lifetime.Token;
        try
        {
            if (Mode.Value == 2)
            {
                GitRemoteUrl remote = GitRemoteUrl.Parse(Url.Value.Trim());
                await _apply(remote, token);
                return true;
            }

            if (Picker.User is not { } owner || Picker.Service is not { } service)
                return false;

            (string Owner, string Name)? creationKey = null;
            GitRepositorySummary? repository = Picker.SelectedRepository.Value;
            if (Mode.Value == 0)
            {
                if (_creationOwnerId is not null && _creationOwnerId != owner.Profile.Id)
                {
                    Error.Value = Strings.HostedGit_AccountChanged;
                    return false;
                }
                _creationOwnerId = owner.Profile.Id;
                var key = (owner.Profile.Id, Name.Value.Trim());
                creationKey = key;
                if (!_creationIds.TryGetValue(key, out Guid creationId))
                {
                    creationId = Guid.NewGuid();
                    _creationIds.Add(key, creationId);
                }
                repository = await service.CreateAsync(Name.Value, creationId, owner, token);
            }
            if (repository is null)
                return false;

            await HostedGitRepositoryConnection.ConfigureAsync(service, owner, repository, _apply, token);
            if (creationKey is { } created)
                _creationIds.Remove(created);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                if (ex is HostedGitApiException { Error: HostedGitError.AuthenticationRequired })
                    Picker.ShowError(ex);
                else
                    Error.Value = ex.Message;
            }
            return false;
        }
        finally
        {
            if (!_disposed)
                IsBusy.Value = false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _disposables.Dispose();
    }
}
