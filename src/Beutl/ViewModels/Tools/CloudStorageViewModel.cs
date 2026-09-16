using System.Collections.ObjectModel;
using System.Net;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Threading;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Editor.Components.FileBrowserTab;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Logging;
using Beutl.Pages;
using Beutl.Views.Tools;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Refit;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.ViewModels.Tools;

internal sealed record CloudStorageItem(string Id, string Name, bool IsFolder, string Details, Icon Icon = Icon.Document)
{
    public string ToolTip => $"{Name}{Environment.NewLine}{Details}";
}

internal sealed class CloudStorageViewModel : IFileBrowserStorageBrowser, IFileBrowserStorageNavigation
{
    private readonly BeutlApiApplication _clients;
    private readonly Func<SettingsDialogViewModel> _createSettings;
    private readonly CompositeDisposable _disposables = [];
    private readonly ILogger _logger = Log.CreateLogger<CloudStorageViewModel>();
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _load;
    private AuthenticatedUser? _owner;
    private StorageFolderResponse[] _folders = [];
    private string? _folderId;
    private int _page;
    private readonly HashSet<string> _fileIds = new(StringComparer.Ordinal);
    private long _version;
    private bool _disposed;

    public CloudStorageViewModel(
        BeutlApiApplication clients,
        Func<SettingsDialogViewModel> createSettings)
    {
        _clients = clients;
        _createSettings = createSettings;

        // Navigation and refresh may replace an in-flight append request.
        Refresh = new AsyncReactiveCommand(SignedIn).DisposeWith(_disposables);
        Refresh.Subscribe(LoadAsync).DisposeWith(_disposables);
        CycleViewMode = new ReactiveCommand().DisposeWith(_disposables);
        CycleViewMode.Subscribe(() => ViewMode.Value = ViewMode.Value == FileBrowserViewMode.Icon
            ? FileBrowserViewMode.List : FileBrowserViewMode.Icon).DisposeWith(_disposables);
        RetryLoadMore = new AsyncReactiveCommand(SignedIn.CombineLatest(IsLoadingMore,
            (signedIn, loading) => signedIn && !loading)).DisposeWith(_disposables);
        RetryLoadMore.Subscribe(() => LoadMoreAsync(retry: true)).DisposeWith(_disposables);

        clients.AuthenticatedUser.Subscribe(_ =>
        {
            if (Dispatcher.UIThread.CheckAccess())
                AuthenticationChanged();
            else
                Dispatcher.UIThread.Post(AuthenticationChanged);
        }).DisposeWith(_disposables);
    }

    ICommand IFileBrowserStorageBrowser.Refresh => Refresh;
    ICommand IFileBrowserStorageNavigation.CycleViewMode => CycleViewMode;
    IReadOnlyReactiveProperty<FileBrowserViewMode> IFileBrowserStorageNavigation.ViewMode => ViewMode;
    IReadOnlyList<FileBrowserStorageBreadcrumb> IFileBrowserStorageNavigation.Breadcrumbs => Breadcrumbs;
    public ObservableCollection<FileBrowserStorageBreadcrumb> Breadcrumbs { get; } = [];
    public ReactivePropertySlim<FileBrowserViewMode> ViewMode { get; } = new(FileBrowserViewMode.Icon);
    public ReactiveCommand CycleViewMode { get; }
    public Control CreateView() => new CloudStorageView { DataContext = this };
    public ObservableCollection<CloudStorageItem> Items { get; } = [];
    public ReactivePropertySlim<bool> SignedIn { get; } = new();
    public ReactivePropertySlim<bool> IsLoading { get; } = new();
    public ReactivePropertySlim<bool> IsEmpty { get; } = new();
    public ReactivePropertySlim<bool> HasUsage { get; } = new();
    public ReactivePropertySlim<bool> IsLoadingMore { get; } = new();
    public ReactivePropertySlim<bool> HasMore { get; } = new();
    public ReactivePropertySlim<string?> LoadMoreError { get; } = new();
    public ReactivePropertySlim<string> UsageText { get; } = new("");
    public ReactivePropertySlim<double> UsagePercent { get; } = new();
    public ReactivePropertySlim<string?> Error { get; } = new();
    public AsyncReactiveCommand Refresh { get; }
    public AsyncReactiveCommand RetryLoadMore { get; }

    private void AuthenticationChanged()
    {
        if (_disposed) return;
        ++_version;
        _load?.Cancel();
        _load = null;
        _folders = [];
        _folderId = null;
        _page = 0;
        Breadcrumbs.Clear();
        Breadcrumbs.Add(new(Strings.CloudStorage, null));
        Error.Value = null;
        IsLoading.Value = false;
        IsLoadingMore.Value = false;
        ClearListing();
        _owner = _clients.AuthenticatedUser.Value;
        SignedIn.Value = _owner != null;
        if (SignedIn.Value) _ = LoadAsync();
    }

    internal Task LoadAsync() => LoadPageAsync(append: false);

    internal Task LoadMoreAsync(bool retry = false)
    {
        if (_disposed || IsLoading.Value || IsLoadingMore.Value || !HasMore.Value
            || (!retry && LoadMoreError.Value != null))
            return Task.CompletedTask;
        return LoadPageAsync(append: true);
    }

    private async Task LoadPageAsync(bool append)
    {
        if (_disposed || _owner is not { } user
            || !ReferenceEquals(_clients.AuthenticatedUser.Value, user)) return;
        _load?.Cancel();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _load = operation;
        if (!append)
        {
            ++_version;
            IsLoading.Value = true;
            IsLoadingMore.Value = false;
            Error.Value = null;
            ClearListing();
        }
        else
        {
            IsLoadingMore.Value = true;
            LoadMoreError.Value = null;
        }
        long version = _version;
        string? folder = _folderId;
        int page = append ? _page + 1 : 1;
        try
        {
            var result = await _clients.SendAuthenticatedAsync(
                (authorization, token) => _clients.Storage.GetStorage(authorization, token, folder, page),
                operation.Token, user);
            if (!IsCurrent()) return;
            if (append && result.Value.FolderId != folder)
            {
                // A deleted folder resolves to the root on the server. Never mix its files
                // into the old folder's accumulated listing.
                _clients.CommitForAuthenticatedUser(user, () => _folderId = result.Value.FolderId, operation.Token);
                await LoadAsync();
                return;
            }
            _clients.CommitForAuthenticatedUser(user,
                () => Apply(result.Value, append, page), operation.Token);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!IsCurrent()) return;
            _logger.LogError(ex, "Failed to load cloud storage.");
            if (append)
                LoadMoreError.Value = Strings.CloudStorageLoadMoreFailed;
            else
                Error.Value = ex is ApiException { StatusCode: HttpStatusCode.NotFound or HttpStatusCode.NotImplemented }
                    ? Strings.CloudStorageUnavailable : Strings.CloudStorageLoadFailed;
        }
        finally
        {
            if (ReferenceEquals(_load, operation))
            {
                _load = null;
                if (!_disposed && version == _version)
                {
                    IsLoading.Value = false;
                    IsLoadingMore.Value = false;
                }
            }
        }

        bool IsCurrent() => !_disposed && version == _version
            && ReferenceEquals(_clients.AuthenticatedUser.Value, user);
    }

    private void Apply(StorageResponse response, bool append, int requestedPage)
    {
        if (!append)
        {
            _folders = response.Folders;
            _folderId = response.FolderId;
            UpdateBreadcrumbs();
            foreach (var folder in _folders.Where(x => x.ParentId == _folderId))
                Items.Add(new(folder.Id, folder.Name, true, Strings.CloudStorageFolder, Icon.Folder));
        }

        // Offset pagination can overlap when files change between requests. A clamped page
        // signals that the list shrank; do not keep requesting that same last page.
        if (response.Page >= requestedPage)
        {
            _page = response.Page;
            foreach (var file in response.Files)
            {
                if (!_fileIds.Add(file.Id)) continue;
                string visibility = file.Visibility switch
                {
                    "PUBLIC" => Strings.CloudStoragePublic,
                    "PRIVATE" => Strings.CloudStoragePrivate,
                    "DEDICATED" => Strings.CloudStorageDedicated,
                    _ => file.Visibility,
                };
                string details = $"{FormatBytes(file.Size)} · {file.CreatedAt.ToLocalTime():d} · {visibility}";
                Icon icon = file.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? Icon.Image
                    : file.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ? Icon.Video
                    : file.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ? Icon.MusicNote1
                    : Icon.Document;
                Items.Add(new(file.Id, file.Name, false, details, icon));
            }
        }
        HasMore.Value = response.Page >= requestedPage && response.Page < response.PageCount;
        IsEmpty.Value = Items.Count == 0 && !HasMore.Value;

        var usage = response.Usage;
        string plan = usage.Plan?.ToUpperInvariant() ?? Strings.CloudStorageFree;
        UsageText.Value = string.Format(Strings.CloudStorageUsage,
            FormatBytes(usage.UsedBytes), FormatBytes(usage.QuotaBytes), usage.FileCount, usage.FileCountLimit, plan);
        UsagePercent.Value = usage.QuotaBytes > 0
            ? Math.Clamp(100.0 * usage.UsedBytes / usage.QuotaBytes, 0, 100)
            : 0;
        HasUsage.Value = true;
    }

    private void UpdateBreadcrumbs()
    {
        Breadcrumbs.Clear();
        var path = new List<FileBrowserStorageBreadcrumb>();
        var visited = new HashSet<string>();
        string? id = _folderId;
        while (id != null && visited.Add(id) && _folders.FirstOrDefault(x => x.Id == id) is { } folder)
        {
            path.Add(new(folder.Name, folder.Id));
            id = folder.ParentId;
        }
        path.Add(new(Strings.CloudStorage, null));
        path.Reverse();
        foreach (var item in path) Breadcrumbs.Add(item);
    }

    internal Task OpenFolderAsync(CloudStorageItem? item) => item is { IsFolder: true }
        && Items.Contains(item) ? NavigateAsync(item.Id) : Task.CompletedTask;

    public Task NavigateToAsync(FileBrowserStorageBreadcrumb breadcrumb) => Breadcrumbs.Contains(breadcrumb)
        ? NavigateAsync(breadcrumb.FolderId) : Task.CompletedTask;

    private Task NavigateAsync(string? folder)
    {
        _folderId = folder;
        return LoadAsync();
    }

    internal async Task OpenAccountSettingsAsync(Window owner)
    {
        using var vm = _createSettings();
        var dialog = new SettingsDialog { DataContext = vm };
        vm.GoToAccountSettingsPage();
        await dialog.ShowDialog(owner);
    }

    private void ClearListing()
    {
        Items.Clear();
        IsEmpty.Value = false;
        HasUsage.Value = false;
        UsageText.Value = "";
        UsagePercent.Value = 0;
        _page = 0;
        _fileIds.Clear();
        HasMore.Value = false;
        LoadMoreError.Value = null;
    }

    internal static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KiB", "MiB", "GiB", "TiB"];
        double size = Math.Max(0, bytes);
        int unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _owner = null;
        ++_version;
        _lifetime.Cancel();
        _disposables.Dispose();
        _lifetime.Dispose();
        Items.Clear();
        SignedIn.Dispose();
        IsLoading.Dispose();
        IsEmpty.Dispose();
        HasUsage.Dispose();
        IsLoadingMore.Dispose();
        HasMore.Dispose();
        LoadMoreError.Dispose();
        ViewMode.Dispose();
        Breadcrumbs.Clear();
        _fileIds.Clear();
        UsageText.Dispose();
        UsagePercent.Dispose();
        Error.Dispose();
    }
}
