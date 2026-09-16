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
    private readonly ObservableCollection<FileBrowserStorageBreadcrumb> _breadcrumbs = [];
    private CancellationTokenSource? _load;
    private AuthenticatedUser? _owner;
    private StorageFolderResponse[] _folders = [];
    private string? _folderId;
    private int _page;
    private readonly HashSet<string> _fileIds = new(StringComparer.Ordinal);
    private long _version;
    private bool _disposed;
    private readonly Dictionary<string, (StorageResponse Response, DateTimeOffset Expires, long Access)> _folderCache = [];
    private long _cacheAccess;
    private readonly Func<DateTimeOffset> _utcNow;
    private FolderPrefetch? _prefetch;

    public CloudStorageViewModel(
        BeutlApiApplication clients,
        Func<SettingsDialogViewModel> createSettings,
        Func<DateTimeOffset>? utcNow = null)
    {
        _clients = clients;
        _createSettings = createSettings;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        Breadcrumbs = new(_breadcrumbs);
        ShowPlaceholders = IsLoadingVisible.CombineLatest(HasUsage, (loading, loaded) => loading && !loaded)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);

        // Navigation and refresh may replace an in-flight append request.
        Refresh = new AsyncReactiveCommand(SignedIn).DisposeWith(_disposables);
        Refresh.Subscribe(LoadAsync).DisposeWith(_disposables);
        CycleViewMode = new ReactiveCommand().DisposeWith(_disposables);
        CycleViewMode.Subscribe(() => ViewMode.Value = ViewMode.Value == FileBrowserViewMode.Icon
            ? FileBrowserViewMode.List : FileBrowserViewMode.Icon).DisposeWith(_disposables);
        RetryLoadMore = new AsyncReactiveCommand(SignedIn.CombineLatest(IsLoadingMore,
            (signedIn, loading) => signedIn && !loading)).DisposeWith(_disposables);
        RetryLoadMore.Subscribe(() => LoadMoreAsync(retry: true)).DisposeWith(_disposables);

        clients.AuthenticatedUser.Subscribe(user =>
        {
            if (Dispatcher.UIThread.CheckAccess())
                AuthenticationChanged(user);
            else
                Dispatcher.UIThread.Post(() => AuthenticationChanged(user));
        }).DisposeWith(_disposables);
    }

    ICommand IFileBrowserStorageBrowser.Refresh => Refresh;
    ICommand IFileBrowserStorageNavigation.CycleViewMode => CycleViewMode;
    IReadOnlyReactiveProperty<FileBrowserViewMode> IFileBrowserStorageNavigation.ViewMode => ViewMode;
    public ReadOnlyObservableCollection<FileBrowserStorageBreadcrumb> Breadcrumbs { get; }
    public ReactivePropertySlim<FileBrowserViewMode> ViewMode { get; } = new(FileBrowserViewMode.Icon);
    public ReactiveCommand CycleViewMode { get; }
    public Control CreateView() => new CloudStorageView { DataContext = this };
    public ObservableCollection<CloudStorageItem> Items { get; } = [];
    public ReactivePropertySlim<bool> SignedIn { get; } = new();
    public ReactivePropertySlim<bool> IsLoading { get; } = new();
    public ReactivePropertySlim<bool> IsLoadingVisible { get; } = new();
    public ReactivePropertySlim<bool> IsLoadingMoreVisible { get; } = new();
    public ReadOnlyReactivePropertySlim<bool> ShowPlaceholders { get; }
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

    private void AuthenticationChanged(AuthenticatedUser? user)
    {
        // A newer account may already have replaced this notification while the UI was busy.
        if (_disposed || !ReferenceEquals(_clients.AuthenticatedUser.Value, user)) return;
        ClearFolderCache();
        ++_version;
        _load?.Cancel();
        _load = null;
        _folders = [];
        _folderId = null;
        _page = 0;
        _breadcrumbs.Clear();
        _breadcrumbs.Add(new(Strings.CloudStorage, null));
        Error.Value = null;
        IsLoading.Value = false;
        IsLoadingMore.Value = false;
        IsLoadingVisible.Value = false;
        IsLoadingMoreVisible.Value = false;
        ClearListing();
        _owner = user;
        SignedIn.Value = _owner != null;
        if (SignedIn.Value) _ = LoadAsync();
    }

    internal Task LoadAsync()
    {
        _folderCache.Remove(_folderId ?? "");
        return LoadPageAsync(append: false);
    }

    internal Task LoadMoreAsync(bool retry = false)
    {
        if (_disposed || IsLoading.Value || IsLoadingMore.Value || !HasMore.Value || Error.Value != null
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
        IsLoadingVisible.Value = false;
        IsLoadingMoreVisible.Value = false;
        if (!append)
        {
            ++_version;
            IsLoading.Value = true;
            IsLoadingMore.Value = false;
            Error.Value = null;
            LoadMoreError.Value = null;
            // Keep the current folder visible until a refresh succeeds. Navigation and account
            // changes clear it explicitly so files never appear under the wrong path or account.
        }
        else
        {
            IsLoadingMore.Value = true;
            LoadMoreError.Value = null;
        }
        long version = _version;
        string? folder = _folderId;
        int page = append ? _page + 1 : 1;
        _ = ShowLoadingAfterDelayAsync(operation);
        try
        {
            StorageResponse response;
            if (!append && _prefetch is { } prefetch && prefetch.FolderId == folder && ReferenceEquals(prefetch.User, user))
            {
                // Promote the hover/focus request instead of starting the same request again.
                _prefetch = null;
                using var cancellation = operation.Token.Register(prefetch.Cancel);
                response = await prefetch.Request.WaitAsync(operation.Token);
            }
            else
            {
                response = await FetchPageAsync(user, folder, page, operation.Token);
            }
            if (!IsCurrent()) return;
            if (append && response.FolderId != folder)
            {
                // A deleted folder resolves to the root on the server. Never mix its files
                // into the old folder's accumulated listing.
                _clients.CommitForAuthenticatedUser(user, () => ResetFolder(response.FolderId), operation.Token);
                await LoadAsync();
                return;
            }
            _clients.CommitForAuthenticatedUser(user,
                () => Apply(response, append, page), operation.Token);
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
                    IsLoadingVisible.Value = false;
                    IsLoadingMoreVisible.Value = false;
                }
            }
        }

        bool IsCurrent() => !_disposed && version == _version
            && ReferenceEquals(_clients.AuthenticatedUser.Value, user);
    }

    private async Task ShowLoadingAfterDelayAsync(CancellationTokenSource operation)
    {
        try
        {
            await Task.Delay(200, operation.Token);
            if (_disposed || !ReferenceEquals(_load, operation)) return;
            IsLoadingVisible.Value = IsLoading.Value;
            IsLoadingMoreVisible.Value = IsLoadingMore.Value;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
    }

    private void Apply(StorageResponse response, bool append, int requestedPage, bool cache = true)
    {
        if (!append)
        {
            Items.Clear();
            _fileIds.Clear();
            _page = 0;
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
        if (!append && cache) CacheFolder(response);
    }

    private async Task<StorageResponse> FetchPageAsync(AuthenticatedUser user, string? folder, int page, CancellationToken token)
    {
        var result = await _clients.SendAuthenticatedAsync(
            (authorization, cancellation) => _clients.Storage.GetStorage(authorization, cancellation, folder, page), token, user);
        return result.Value;
    }

    private bool TryGetCachedFolder(string? folder, out StorageResponse response)
    {
        string key = folder ?? "";
        if (_folderCache.Remove(key, out var cached) && cached.Expires > _utcNow())
        {
            _folderCache.Add(key, (cached.Response, cached.Expires, ++_cacheAccess));
            response = cached.Response;
            return true;
        }
        response = null!;
        return false;
    }

    private void CacheFolder(StorageResponse response)
    {
        string key = response.FolderId ?? "";
        _folderCache.Remove(key);
        _folderCache.Add(key, (response, _utcNow().AddSeconds(30), ++_cacheAccess));
        while (_folderCache.Count > 8) _folderCache.Remove(_folderCache.MinBy(x => x.Value.Access).Key);
    }

    internal async Task PrefetchFolderAsync(CloudStorageItem item)
    {
        if (_disposed || IsLoading.Value || !item.IsFolder || !Items.Contains(item)
            || _owner is not { } user || !ReferenceEquals(_clients.AuthenticatedUser.Value, user)
            || TryGetCachedFolder(item.Id, out _) || _prefetch?.FolderId == item.Id) return;
        _prefetch?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var prefetch = new FolderPrefetch(item.Id, user, cancellation,
            FetchPageAsync(user, item.Id, 1, cancellation.Token));
        _prefetch = prefetch;
        await ObservePrefetchAsync(prefetch);
    }

    private async Task ObservePrefetchAsync(FolderPrefetch prefetch)
    {
        try
        {
            var response = await prefetch.Request;
            if (!_disposed && ReferenceEquals(_prefetch, prefetch) && !prefetch.Cancellation.IsCancellationRequested
                && ReferenceEquals(_clients.AuthenticatedUser.Value, prefetch.User)
                && response.FolderId == prefetch.FolderId)
                CacheFolder(response);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _logger.LogDebug(ex, "Storage folder prefetch failed."); }
        finally
        {
            if (ReferenceEquals(_prefetch, prefetch)) _prefetch = null;
            prefetch.Cancellation.Dispose();
        }
    }

    private void ClearFolderCache()
    {
        _prefetch?.Cancel();
        _prefetch = null;
        _folderCache.Clear();
    }

    private sealed record FolderPrefetch(string FolderId, AuthenticatedUser User,
        CancellationTokenSource Cancellation, Task<StorageResponse> Request)
    {
        // The observer disposes the CTS only after Request has completed. A promoted request's
        // cancellation registration may outlive that observer, so completed requests need no cancel.
        public void Cancel() { if (!Request.IsCompleted) Cancellation.Cancel(); }
    }

    private void UpdateBreadcrumbs()
    {
        _breadcrumbs.Clear();
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
        foreach (var item in path) _breadcrumbs.Add(item);
    }

    internal Task OpenFolderAsync(CloudStorageItem? item) => item is { IsFolder: true }
        && Items.Contains(item) ? NavigateAsync(item.Id) : Task.CompletedTask;

    public Task NavigateToAsync(FileBrowserStorageBreadcrumb breadcrumb) => Breadcrumbs.Contains(breadcrumb)
        ? NavigateAsync(breadcrumb.FolderId) : Task.CompletedTask;

    private Task NavigateAsync(string? folder)
    {
        if (_disposed || _owner is not { } user || !ReferenceEquals(_clients.AuthenticatedUser.Value, user))
            return Task.CompletedTask;
        if (_folderId == folder) return LoadAsync();
        _load?.Cancel();
        _load = null;
        ++_version;
        IsLoading.Value = false;
        IsLoadingMore.Value = false;
        IsLoadingVisible.Value = false;
        IsLoadingMoreVisible.Value = false;
        Error.Value = null;
        ResetFolder(folder);
        if (TryGetCachedFolder(folder, out var cached))
        {
            _clients.CommitForAuthenticatedUser(user, () => Apply(cached, false, 1, cache: false), _lifetime.Token);
            return Task.CompletedTask;
        }
        return LoadAsync();
    }

    private void ResetFolder(string? folder)
    {
        _folderId = folder;
        ClearListing();
        UpdateBreadcrumbs();
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
        ClearFolderCache();
        _owner = null;
        ++_version;
        _lifetime.Cancel();
        _disposables.Dispose();
        _lifetime.Dispose();
        Items.Clear();
        SignedIn.Dispose();
        IsLoading.Dispose();
        IsLoadingVisible.Dispose();
        IsLoadingMoreVisible.Dispose();
        IsEmpty.Dispose();
        HasUsage.Dispose();
        IsLoadingMore.Dispose();
        HasMore.Dispose();
        LoadMoreError.Dispose();
        ViewMode.Dispose();
        _breadcrumbs.Clear();
        _fileIds.Clear();
        UsageText.Dispose();
        UsagePercent.Dispose();
        Error.Dispose();
    }
}
