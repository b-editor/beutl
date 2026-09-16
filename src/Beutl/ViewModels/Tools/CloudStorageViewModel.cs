using System.Collections.ObjectModel;
using System.Net;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Threading;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
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

internal sealed record CloudStorageItem(string Id, string Name, bool IsFolder, string Details, Icon Icon = Icon.Document, StorageEntryResponse? Entry = null, string Location = "")
{
    public string SizeText => CloudStorageViewModel.FormatBytes(Entry?.Size ?? 0);
    public string VisibilityText => CloudStorageViewModel.VisibilityLabel(Entry?.Visibility ?? "");
    public string CreatedText => Entry?.CreatedAt.ToLocalTime().ToString("g") ?? "";
    public bool Can(string action) => Entry?.Actions.Contains(action, StringComparer.Ordinal) == true;
    public string ToolTip => $"{Name}{Environment.NewLine}{Details}";
}

internal sealed partial class CloudStorageViewModel : IFileBrowserStorageBrowser, IFileBrowserStorageNavigation
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
    private string? _nextCursor;
    private CancellationTokenSource? _usageLoad;
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
        ShowPlaceholders = IsLoadingVisible.CombineLatest(HasListing, (loading, loaded) => loading && !loaded)
            .ToReadOnlyReactivePropertySlim().DisposeWith(_disposables);

        // Navigation and refresh may replace an in-flight append request.
        Refresh = new AsyncReactiveCommand(SignedIn.CombineLatest(IsBusy, (signedIn, busy) => signedIn && !busy)).DisposeWith(_disposables);
        Refresh.Subscribe(() => Task.WhenAll(LoadAsync(), LoadUsageAsync())).DisposeWith(_disposables);
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
    public ReactivePropertySlim<bool> HasListing { get; } = new();
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
        CancelActions();
        ClearFolderCache();
        ++_version;
        _load?.Cancel();
        _load = null;
        _folders = [];
        _folderId = null;
        _nextCursor = null;
        _breadcrumbs.Clear();
        _breadcrumbs.Add(new(Strings.CloudStorage, null));
        Error.Value = null;
        IsLoading.Value = false;
        IsLoadingMore.Value = false;
        IsLoadingVisible.Value = false;
        IsLoadingMoreVisible.Value = false;
        ClearListing(clearUsage: true);
        _usageLoad?.Cancel();
        _owner = user;
        SignedIn.Value = _owner != null;
        if (SignedIn.Value)
        {
            _ = LoadAsync();
            _ = LoadUsageAsync();
        }
    }

    internal Task LoadAsync()
    {
        if (_disposed || IsBusy.Value) return Task.CompletedTask;
        _folderCache.Remove(_folderId ?? "");
        return LoadPageAsync(append: false);
    }

    internal Task LoadMoreAsync(bool retry = false)
    {
        if (_disposed || IsBusy.Value || IsLoading.Value || IsLoadingMore.Value || !HasMore.Value || Error.Value != null
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
        string? cursor = append ? _nextCursor : null;
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
                response = await FetchPageAsync(user, folder, cursor, operation.Token);
            }
            if (!IsCurrent()) return;
            if (append && response.ParentId != folder)
            {
                // A deleted folder resolves to the root on the server. Never mix its files
                // into the old folder's accumulated listing.
                _clients.CommitForAuthenticatedUser(user, () => ResetFolder(response.ParentId), operation.Token);
                await LoadAsync();
                return;
            }
            _clients.CommitForAuthenticatedUser(user,
                () => Apply(response, append, cursor), operation.Token);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound && folder != null && IsCurrent())
        {
            ClearFolderCache();
            ResetFolder(null);
            await LoadAsync();
        }
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

    private void Apply(StorageResponse response, bool append, string? requestedCursor, bool cache = true)
    {
        if (!append)
        {
            Items.Clear();
            _fileIds.Clear();
            _folderId = response.ParentId;
            _folders = response.Path.Concat(response.Entries.Where(x => x.Kind == "folder")
                .Select(x => new StorageFolderResponse { Id = x.Id, Name = x.Name, ParentId = x.ParentId }))
                .DistinctBy(x => x.Id).ToArray();
            UpdateBreadcrumbs();
        }
        foreach (var entry in response.Entries)
        {
            if (!_fileIds.Add($"{entry.Kind}:{entry.Id}")) continue;
            bool folder = entry.Kind == "folder";
            string visibility = VisibilityLabel(entry.Visibility);
            string details = folder ? Strings.CloudStorageFolder
                : $"{FormatBytes(entry.Size)} · {entry.CreatedAt.ToLocalTime():d} · {visibility}";
            Icon icon = folder ? Icon.Folder
                : entry.MimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? Icon.Image
                : entry.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase) ? Icon.Video
                : entry.MimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) ? Icon.MusicNote1 : Icon.Document;
            Items.Add(new(entry.Id, entry.Name, folder, details, icon, entry, string.Join(" / ", Breadcrumbs.Select(x => x.Name))));
            if (folder && _folders.All(x => x.Id != entry.Id))
                _folders = [.. _folders, new StorageFolderResponse { Id = entry.Id, Name = entry.Name, ParentId = entry.ParentId }];
        }
        _nextCursor = response.NextCursor;
        HasMore.Value = !string.IsNullOrEmpty(_nextCursor) && _nextCursor != requestedCursor;
        IsEmpty.Value = Items.Count == 0 && !HasMore.Value;
        HasListing.Value = true;
        if (!append && cache) CacheFolder(response);
    }

    internal static string VisibilityLabel(string visibility) => visibility switch
    {
        "PUBLIC" => Strings.CloudStoragePublic,
        "PRIVATE" => Strings.CloudStoragePrivate,
        "DEDICATED" => Strings.CloudStorageDedicated,
        _ => visibility,
    };

    private async Task<StorageResponse> FetchPageAsync(AuthenticatedUser user, string? folder, string? cursor, CancellationToken token)
    {
        var result = await _clients.SendAuthenticatedAsync(
            (authorization, cancellation) => _clients.Storage.GetEntries(authorization, cancellation, folder, cursor), token, user);
        return result.Value;
    }

    internal async Task LoadUsageAsync()
    {
        if (_disposed || _owner is not { } user || !ReferenceEquals(_clients.AuthenticatedUser.Value, user)) return;
        _usageLoad?.Cancel();
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _usageLoad = operation;
        try
        {
            var result = await _clients.SendAuthenticatedAsync((authorization, token) => _clients.Storage.GetUsage(authorization, token), operation.Token, user);
            _clients.CommitForAuthenticatedUser(user, () =>
            {
                if (_disposed || !ReferenceEquals(_usageLoad, operation)) return;
                var usage = result.Value;
                string plan = usage.Plan?.ToUpperInvariant() ?? Strings.CloudStorageFree;
                UsageText.Value = string.Format(Strings.CloudStorageUsage, FormatBytes(usage.UsedBytes), FormatBytes(usage.QuotaBytes), usage.FileCount, usage.FileCountLimit, plan);
                UsagePercent.Value = usage.QuotaBytes > 0 ? Math.Clamp(100.0 * usage.UsedBytes / usage.QuotaBytes, 0, 100) : 0;
                HasUsage.Value = true;
            }, operation.Token);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception ex) { if (!_disposed) _logger.LogDebug(ex, "Could not update storage usage."); }
        finally { if (ReferenceEquals(_usageLoad, operation)) _usageLoad = null; }
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
        string key = response.ParentId ?? "";
        _folderCache.Remove(key);
        _folderCache.Add(key, (response, _utcNow().AddSeconds(30), ++_cacheAccess));
        while (_folderCache.Count > 8) _folderCache.Remove(_folderCache.MinBy(x => x.Value.Access).Key);
    }

    internal async Task PrefetchFolderAsync(CloudStorageItem item)
    {
        if (_disposed || IsBusy.Value || IsLoading.Value || !item.IsFolder || !Items.Contains(item)
            || _owner is not { } user || !ReferenceEquals(_clients.AuthenticatedUser.Value, user)
            || TryGetCachedFolder(item.Id, out _) || _prefetch?.FolderId == item.Id) return;
        _prefetch?.Cancel();
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        var prefetch = new FolderPrefetch(item.Id, user, cancellation,
            FetchPageAsync(user, item.Id, null, cancellation.Token));
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
                && response.ParentId == prefetch.FolderId)
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
        if (_disposed || IsBusy.Value || _owner is not { } user || !ReferenceEquals(_clients.AuthenticatedUser.Value, user))
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
            try
            {
                _clients.CommitForAuthenticatedUser(user, () => Apply(cached, false, null, cache: false), _lifetime.Token);
            }
            catch (AuthenticationRequiredException)
            {
                // Account notifications from another thread may still be queued on the UI thread.
                ClearFolderCache();
            }
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

    private void ClearListing(bool clearUsage = false)
    {
        Items.Clear();
        IsEmpty.Value = false;
        HasListing.Value = false;
        if (clearUsage)
        {
            HasUsage.Value = false;
            UsageText.Value = "";
            UsagePercent.Value = 0;
        }
        _nextCursor = null;
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
        CancelActions();
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
        HasListing.Dispose();
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
        IsBusy.Dispose();
        ActionError.Dispose();
        DetailsItem.Dispose();
    }
}
