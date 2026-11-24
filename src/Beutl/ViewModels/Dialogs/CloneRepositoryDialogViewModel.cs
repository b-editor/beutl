using System.Reactive.Disposables;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Editor.Components.VersionControl.ViewModels;
using Beutl.Editor.VersionControl;
using Beutl.Services;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Dialogs;

internal sealed class CloneRepositoryDialogViewModel : IDisposable
{
    private readonly GitRepositoryCloner _cloner;
    private readonly RepositoryOpenRequest? _request;
    private readonly CancellationTokenSource _lifetime;
    private readonly CompositeDisposable _disposables = [];
    private CancellationTokenSource? _operation;
    private string _suggestedFolder = string.Empty;
    private bool _disposed;

    public CloneRepositoryDialogViewModel(
        BeutlApiApplication? api, ViewConfig config, GitRepositoryCloner cloner,
        RepositoryOpenRequest? request, CancellationToken cancellationToken)
    {
        _cloner = cloner;
        _request = request;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Picker = new HostedRepositoryPickerViewModel(api, _lifetime.Token, request?.RepositoryId).DisposeWith(_disposables);
        Mode = new ReactivePropertySlim<int>().DisposeWith(_disposables);
        Url = new ReactivePropertySlim<string>(string.Empty).DisposeWith(_disposables);
        ParentFolder = new ReactivePropertySlim<string>(CreateNewProjectViewModel.GetDefaultLocation(config)).DisposeWith(_disposables);
        FolderName = new ReactivePropertySlim<string>(string.Empty).DisposeWith(_disposables);
        IsBusy = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        IsCheckingGit = new ReactivePropertySlim<bool>(true).DisposeWith(_disposables);
        GitUnavailable = new ReactivePropertySlim<bool>().DisposeWith(_disposables);
        GitStatus = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        Progress = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        Error = new ReactivePropertySlim<string>().DisposeWith(_disposables);
        Revision = new ReactivePropertySlim<string?>().DisposeWith(_disposables);
        Destination = ParentFolder.CombineLatest(FolderName, GetDestination)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);

        var hasSource = Mode.CombineLatest(Url, Picker.SelectedRepository, Picker.NeedsSignIn,
            static (mode, url, repository, needsSignIn) => mode == 0
                ? repository is not null && !needsSignIn
                : !string.IsNullOrWhiteSpace(url));
        CanClone = hasSource.CombineLatest(Destination, IsBusy, IsCheckingGit, GitUnavailable,
                static (source, path, busy, checking, unavailable) => source && path is not null && !busy && !checking && !unavailable)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);

        Mode.CombineLatest(Picker.SelectedRepository, Url, (mode, repository, url) =>
            {
                Revision.Value = mode == 0 && _request is not null && repository?.Id == _request.RepositoryId.ToString("D")
                    ? _request.Revision : null;
                return mode == 0 ? repository?.Name : GetUrlName(url);
            })
            .Subscribe(SuggestFolder).DisposeWith(_disposables);
        Mode.Subscribe(mode => Picker.SetActive(mode == 0)).DisposeWith(_disposables);
    }

    public HostedRepositoryPickerViewModel Picker { get; }
    public ReactivePropertySlim<int> Mode { get; }
    public ReactivePropertySlim<string> Url { get; }
    public ReactivePropertySlim<string> ParentFolder { get; }
    public ReactivePropertySlim<string> FolderName { get; }
    public ReactivePropertySlim<bool> IsBusy { get; }
    public ReactivePropertySlim<bool> IsCheckingGit { get; }
    public ReactivePropertySlim<bool> GitUnavailable { get; }
    public ReactivePropertySlim<string> GitStatus { get; }
    public ReactivePropertySlim<string> Progress { get; }
    public ReactivePropertySlim<string> Error { get; }
    public ReactivePropertySlim<string?> Revision { get; }
    public ReadOnlyReactivePropertySlim<string?> Destination { get; }
    public ReadOnlyReactivePropertySlim<bool> CanClone { get; }
    public string? ClonedPath { get; private set; }
    public bool CancelRequested { get; private set; }

    public async Task CheckGitAsync()
    {
        try
        {
            GitAvailability availability = await _cloner.GetAvailabilityAsync(_lifetime.Token);
            if (_disposed)
                return;

            GitUnavailable.Value = availability.State != GitAvailabilityState.Installed;
            GitStatus.Value = availability.State switch
            {
                GitAvailabilityState.Installed => string.Empty,
                GitAvailabilityState.VersionTooOld => string.Format(Strings.VersionControl_GitTooOldFormat, availability.Version),
                _ => Strings.VersionControl_GitNotInstalled,
            };
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (!_disposed)
            {
                GitUnavailable.Value = true;
                GitStatus.Value = Strings.VersionControl_GitNotInstalled;
            }
        }
        finally
        {
            if (!_disposed)
                IsCheckingGit.Value = false;
        }
    }

    public async Task<bool> CloneAsync()
    {
        if (_disposed || !CanClone.Value || Destination.Value is not { } destination)
            return false;

        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _operation = operation;
        IsBusy.Value = true;
        CancelRequested = false;
        Progress.Value = Strings.HostedGit_Cloning;
        Error.Value = string.Empty;
        CancellationToken token = operation.Token;
        var progress = new Progress<string>(text =>
        {
            if (!_disposed && ReferenceEquals(_operation, operation) && !token.IsCancellationRequested)
                Progress.Value = text;
        });

        bool hosted = Mode.Value == 0;
        var owner = Picker.User;
        var repository = Picker.SelectedRepository.Value;
        string? revision = Revision.Value;
        string url = Url.Value;
        try
        {
            // Validate before issuing an access token. GitRepositoryCloner checks again when publishing the checkout.
            await Task.Run(() => GitRepositoryCloner.CheckDestination(destination), token);
            if (hosted)
            {
                if (owner is null || repository is null || Picker.Service is not { } service)
                    return false;

                await HostedGitRepositoryConnection.ConfigureAsync(service, owner, repository, async (remote, ct) =>
                {
                    await _cloner.CloneAsync(remote, destination, revision, progress, ct);
                    return true;
                }, token);
            }
            else
            {
                await _cloner.CloneAsync(url, destination, progress: progress, cancellationToken: token);
            }
            ClonedPath = destination;
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            CancelRequested = true;
            return false;
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                if (ex is HostedGitApiException { Error: HostedGitError.AuthenticationRequired })
                    Picker.ShowError(ex);
                else
                    Error.Value = ex is GitOperationException { Stderr.Length: 0 }
                        ? Strings.HostedGit_CloneFailed : ex.Message;
            }
            return false;
        }
        finally
        {
            _operation = null;
            if (!_disposed)
                IsBusy.Value = false;
        }
    }

    public void CancelClone()
    {
        CancelRequested = true;
        Progress.Value = Strings.HostedGit_Canceling;
        _operation?.Cancel();
    }

    private void SuggestFolder(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || (!string.IsNullOrEmpty(FolderName.Value) && FolderName.Value != _suggestedFolder))
            return;

        string sanitized = new string(name.Where(c => !char.IsControl(c) && !Path.GetInvalidFileNameChars().Contains(c)
            && c is not ('<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')).ToArray()).Trim().TrimEnd('.');
        if (sanitized.Length == 0)
            return;
        _suggestedFolder = IsWindowsDeviceName(sanitized) ? $"_{sanitized}" : sanitized;
        FolderName.Value = _suggestedFolder;
    }

    private static string? GetUrlName(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        try
        {
            string cleanUrl = GitRemoteUrl.Parse(url.Trim()).Url;
            string path = Uri.TryCreate(cleanUrl, UriKind.Absolute, out Uri? uri) && !uri.IsFile
                ? Uri.UnescapeDataString(uri.AbsolutePath)
                : cleanUrl;
            string name = path.TrimEnd('/', '\\').Split('/', '\\', ':').Last();
            return name.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? GetDestination(string parent, string folder)
    {
        if (string.IsNullOrWhiteSpace(parent) || !Path.IsPathFullyQualified(parent) || string.IsNullOrWhiteSpace(folder)
            || folder is "." or ".." || folder.Any(char.IsControl) || folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || (OperatingSystem.IsWindows() && (folder.EndsWith('.') || folder.EndsWith(' ') || IsWindowsDeviceName(folder))))
            return null;

        try
        {
            return Path.GetFullPath(Path.Combine(parent, folder));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool IsWindowsDeviceName(string name)
    {
        string stem = name.Split('.', 2)[0].TrimEnd().ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL"
            || (stem.Length == 4 && stem[3] is >= '1' and <= '9' && stem[..3] is ("COM" or "LPT"));
    }

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _disposables.Dispose();
    }
}
