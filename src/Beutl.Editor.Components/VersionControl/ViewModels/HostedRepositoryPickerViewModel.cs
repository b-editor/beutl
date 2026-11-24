using System.Reactive.Disposables;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Language;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.VersionControl.ViewModels;

internal sealed class HostedRepositoryPickerViewModel : IDisposable
{
    private readonly BeutlApiApplication? _api;
    private readonly Guid? _preferredId;
    private readonly CancellationTokenSource _lifetime;
    private readonly CompositeDisposable _disposables = [];
    private CancellationTokenSource? _load;
    private IReadOnlyList<GitRepositorySummary> _all = [];
    private bool _active;
    private bool _loaded;
    private bool _disposed;

    public HostedRepositoryPickerViewModel(BeutlApiApplication? api, CancellationToken cancellationToken, Guid? preferredId = null)
    {
        _api = api;
        _preferredId = preferredId;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Service = api?.GetResource<HostedGitRepositoryService>();
        NeedsSignIn = new ReactivePropertySlim<bool>(User is null).DisposeWith(_disposables);
        CanSignIn = new ReactivePropertySlim<bool>(api is not null).DisposeWith(_disposables);
        Repositories = new ReactivePropertySlim<IReadOnlyList<GitRepositorySummary>>([]).DisposeWith(_disposables);
        SelectedRepository = new ReactivePropertySlim<GitRepositorySummary?>().DisposeWith(_disposables);
        Search = new ReactivePropertySlim<string>(string.Empty).DisposeWith(_disposables);
        Status = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        IsLoading = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        Search.Subscribe(_ => UpdateFilter()).DisposeWith(_disposables);
        if (api is not null)
        {
            api.AuthenticatedUser
                .Subscribe(_ => VersionControlUiThread.Post(OnAccountChanged))
                .DisposeWith(_disposables);
        }
    }

    public HostedGitRepositoryService? Service { get; }
    public AuthenticatedUser? User => _api?.AuthenticatedUser.Value;
    public ReactivePropertySlim<bool> NeedsSignIn { get; }
    public ReactivePropertySlim<bool> CanSignIn { get; }
    public ReactivePropertySlim<IReadOnlyList<GitRepositorySummary>> Repositories { get; }
    public ReactivePropertySlim<GitRepositorySummary?> SelectedRepository { get; }
    public ReactivePropertySlim<string> Search { get; }
    public ReactivePropertySlim<string> Status { get; }
    public ReactivePropertySlim<bool> IsLoading { get; }

    public void SetActive(bool active)
    {
        _active = active;
        if (active && !_loaded && _load is null)
            _ = RefreshAsync();
    }

    public async Task SignInAsync()
    {
        if (_api is null || _disposed || !CanSignIn.Value)
            return;

        CanSignIn.Value = false;
        Status.Value = string.Empty;
        try
        {
            await _api.SignInAsync(_lifetime.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (!_disposed)
                Status.Value = Strings.HostedGit_SignInFailed;
        }
        finally
        {
            if (!_disposed)
                CanSignIn.Value = true;
        }
    }

    public async Task RefreshAsync()
    {
        if (_disposed || User is not { } user || Service is null)
            return;

        using var load = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _load?.Cancel();
        _load = load;
        IsLoading.Value = true;
        Status.Value = string.Empty;
        try
        {
            var repositories = await Service.ListAsync(load.Token, user);
            if (_disposed || load.IsCancellationRequested || !ReferenceEquals(User, user))
                return;

            _loaded = true;
            _all = repositories;
            NeedsSignIn.Value = false;
            UpdateFilter();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (!_disposed && !load.IsCancellationRequested && ReferenceEquals(User, user))
                ShowError(ex);
        }
        finally
        {
            if (ReferenceEquals(_load, load))
            {
                _load = null;
                if (!_disposed)
                    IsLoading.Value = false;
            }
        }
    }

    public void ShowError(Exception exception)
    {
        if (exception is HostedGitApiException { Error: HostedGitError.AuthenticationRequired })
        {
            NeedsSignIn.Value = true;
            Status.Value = string.Empty;
        }
        else
        {
            Status.Value = exception is HostedGitApiException ? exception.Message : Strings.HostedGit_RequestFailed;
        }
    }

    private void OnAccountChanged()
    {
        if (_disposed)
            return;

        _load?.Cancel();
        _load = null;
        IsLoading.Value = false;
        _loaded = false;
        _all = [];
        NeedsSignIn.Value = User is null;
        SelectedRepository.Value = null;
        UpdateFilter();
        if (_active)
            _ = RefreshAsync();
    }

    private void UpdateFilter()
    {
        string? selectionId = SelectedRepository.Value?.Id ?? _preferredId?.ToString("D");
        string search = Search.Value?.Trim() ?? string.Empty;
        IReadOnlyList<GitRepositorySummary> filtered = search.Length == 0 ? _all
            : _all.Where(repo => repo.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)).ToArray();
        Repositories.Value = filtered;
        SelectedRepository.Value = filtered.FirstOrDefault(repo => repo.Id == selectionId);

        Status.Value = !_loaded ? string.Empty
            : selectionId is not null && _all.All(repo => repo.Id != selectionId) && _preferredId is not null
                ? Strings.HostedGit_NotFound
                : filtered.Count != 0 ? string.Empty
                : _all.Count == 0 ? Strings.HostedGit_NoRepositories : Strings.HostedGit_NoSearchResults;
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _disposables.Dispose();
    }
}
