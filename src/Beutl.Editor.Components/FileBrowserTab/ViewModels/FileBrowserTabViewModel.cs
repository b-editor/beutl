using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia.Data.Converters;
using Avalonia.Threading;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Logging;
using Beutl.Media.Decoding;
using Beutl.Services;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.FileBrowserTab.ViewModels;

public sealed partial class FileBrowserTabViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly ILogger _logger = Log.CreateLogger<FileBrowserTabViewModel>();
    private readonly IEditorContext _editorContext;
    private readonly DirectoryWatcherService _directoryWatcher = new();
    private string _rootPath = string.Empty;
    private readonly FavoritesManager _favoritesManager = new();
    private readonly MediaFileSearcher _mediaSearcher = new();
    private string? _projectDirectory;
    private int _decoderRefreshQueued;
    private volatile bool _disposed;

    internal string? ProjectDirectory => _projectDirectory;

    /// <summary>
    /// Shows a confirmation dialog and returns its result. A test replaces it to decide the dialog's
    /// outcome, and to act between the dialog opening and the user confirming it.
    /// </summary>
    internal Func<FAContentDialog, Task<FAContentDialogResult>> ConfirmAsync { get; set; } =
        static dialog => dialog.ShowAsync();

    internal Func<ProcessStartInfo, Task<bool>> LaunchFileManagerAsync { get; set; } = FileManagerLauncher.LaunchAsync;

    public FileBrowserTabViewModel(IEditorContext editorContext)
        : this(editorContext, editorContext.GetService<FileBrowserStorageProviderRegistry>())
    {
    }

    internal FileBrowserTabViewModel(IEditorContext editorContext, FileBrowserStorageProviderRegistry? storageProviders)
    {
        _editorContext = editorContext;
        InitializeStorage(storageProviders);

        // お気に入り変更時にホームビューを更新
        _favoritesManager.Changed += OnFavoritesChanged;

        DecoderRegistry.DecodersChanged += OnDecodersChanged;

        // ディレクトリ変更時にリフレッシュ
        _directoryWatcher.Changed += OnWatchedDirectoryChanged;
        _directoryWatcher.EntriesChanged += RefreshChangedEntries;

        // プロジェクトディレクトリの取得
        _projectDirectory = EditorContextDirectories.GetProjectOrSceneDirectory(_editorContext);

        Header = RootPath.CombineLatest(ActiveStorageProvider,
                (path, provider) => provider?.DisplayName ?? CreateHeader(path))
            .ToReadOnlyReactivePropertySlim(CreateHeader(RootPath.Value))
            .AddTo(_disposables)!;

        RootPath.Subscribe(OnRootPathChanged).AddTo(_disposables);

        // IsHomeView subscriptions replay immediately, so this performs the initial home-view build.
        IsHomeView.Subscribe(OnHomeViewChanged).AddTo(_disposables);

        ViewMode.Subscribe(OnViewModeChanged).AddTo(_disposables);
    }

    private void OnFavoritesChanged()
    {
        if (IsHomeView.Value)
        {
            _favoritesManager.RefreshFavoriteItems();
        }
    }

    private void OnWatchedDirectoryChanged()
    {
        // A debounced refresh can arrive after disposal.
        if (_disposed)
            return;

        Refresh();
    }

    private void OnRootPathChanged(string path)
    {
        _rootPath = path;
        if (!string.IsNullOrEmpty(path))
        {
            IsHomeView.Value = false;
        }
        UpdateBreadcrumbItems(path);
        RefreshItems();
        _directoryWatcher.Watch(path);
    }

    private void OnHomeViewChanged(bool isHome)
    {
        if (isHome)
        {
            RootPath.Value = string.Empty;
            RefreshHomeView();
            _directoryWatcher.Watch(_projectDirectory);
        }
    }

    private void OnViewModeChanged(FileBrowserViewMode mode)
    {
        if (!IsHomeView.Value)
        {
            RefreshItems();
        }
    }

    public ToolTabExtension Extension => FileBrowserTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; }

    public ReactiveProperty<FileBrowserViewMode> ViewMode { get; } = new(FileBrowserViewMode.Icon);

    public ReactiveProperty<string> RootPath { get; } = new(string.Empty);

    public ObservableCollection<BreadcrumbPathItem> BreadcrumbItems { get; } = [];

    public ReactivePropertySlim<bool> IsHomeView { get; } = new(true);

    // ファイル/フォルダの一覧（フラット表示用）
    public ObservableCollection<FileSystemItemViewModel> Items { get; } = [];

    // ツリー表示用のルートアイテム
    public ObservableCollection<FileSystemItemViewModel> TreeRootItems { get; } = [];

    public ObservableCollection<FileSystemItemViewModel> SelectedItems { get; } = [];

    public ReadOnlyObservableCollection<string> Favorites => _favoritesManager.Favorites;

    public ObservableCollection<FileSystemItemViewModel> FavoriteItems => _favoritesManager.FavoriteItems;

    public ObservableCollection<FileSystemItemViewModel> ProjectDirectoryItems { get; } = [];

    public ObservableCollection<FileSystemItemViewModel> MediaFileItems => _mediaSearcher.MediaFileItems;

    public ReactivePropertySlim<bool> IsLoadingMediaFiles => _mediaSearcher.IsLoadingMediaFiles;

    public ReactivePropertySlim<bool> HasNoMediaFiles => _mediaSearcher.HasNoMediaFiles;

    public ReactivePropertySlim<bool> IsFavoritesIconView { get; } = new(false);

    public ReactivePropertySlim<bool> IsProjectDirIconView { get; } = new(false);

    public ReactivePropertySlim<bool> IsMediaFilesIconView { get; } = new(true);

    // Empty RootPath denotes the home view.
    internal static string CreateHeader(string rootPath)
    {
        if (string.IsNullOrEmpty(rootPath))
            return Strings.FileBrowser;

        string trimmed = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name = Path.GetFileName(trimmed);
        // Filesystem roots have no filename component.
        return string.IsNullOrEmpty(name) ? rootPath : name;
    }

    // Each item classifies itself once, when it is built. Decoding extensions register on a
    // background task after the tab is already live, so anything materialized before that keeps a
    // non-media icon and no thumbnail until the list is rebuilt.
    private void OnDecodersChanged(object? sender, EventArgs e)
    {
        if (_disposed || Interlocked.Exchange(ref _decoderRefreshQueued, 1) != 0)
            return;

        Dispatcher.UIThread.Post(
            () =>
            {
                Interlocked.Exchange(ref _decoderRefreshQueued, 0);

                // The tab can close between the post and the dispatcher running it; refreshing then
                // would repopulate disposed managers and start per-file work for a closed tab.
                if (!_disposed)
                {
                    // A file probed before its decoder registered cached a size-only placeholder and
                    // no thumbnail; rebuilding the items would just read those back.
                    FileThumbnailService.Instance.ClearCache();
                    Refresh();
                }
            },
            DispatcherPriority.Background);
    }

    private void RefreshChangedEntries(IReadOnlyCollection<string> paths)
    {
        if (_disposed) return;

        try
        {
            // A deleted directory can no longer be distinguished from a temporary file by its
            // attributes. Refresh home results if a previously displayed favorite/media path is affected.
            if (IsHomeView.Value && ShouldRebuildHomeView(paths))
            {
                RefreshHomeView();
                return;
            }

            string? root = IsHomeView.Value ? _projectDirectory : _rootPath;
            var items = IsHomeView.Value ? ProjectDirectoryItems
                : ViewMode.Value == FileBrowserViewMode.Tree ? TreeRootItems : Items;
            // A recursive deletion reports children too. Remove vanished parent entries before
            // inspecting their children so one missing directory cannot abort the batch.
            foreach (string directory in paths.Select(Path.GetDirectoryName).OfType<string>()
                         .Distinct(StringComparer.Ordinal).OrderBy(directory => directory.Length))
            {
                if (!string.IsNullOrEmpty(root)
                    && string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), directory, StringComparison.Ordinal)
                    && FileSystemEnumerator.HasEntriesChanged(items, root))
                {
                    Refresh();
                    return;
                }

                // Check only the affected directory, including collapsed tree placeholders.
                // Unchanged autosaves keep the existing items, selection and thumbnail work.
                foreach (var item in items)
                    item.RefreshEntriesForDirectory(directory);
                if (IsHomeView.Value)
                    foreach (var item in FavoriteItems)
                        item.RefreshEntriesForDirectory(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to check file browser entries after a save");
        }
    }

    private bool ShouldRebuildHomeView(IReadOnlyCollection<string> paths)
    {
        return paths.Any(path =>
            FavoriteItems.Any(item => string.Equals(item.FullPath, path, StringComparison.Ordinal)
                && !(item.IsDirectory ? Directory.Exists(path) : File.Exists(path)))
            || MediaFileItems.Any(item => item.FullPath.StartsWith(
                path + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
    }

    private void RefreshItems()
    {
        FileSystemEnumerator.DisposeAndClear(Items);
        FileSystemEnumerator.DisposeAndClear(TreeRootItems);
        SelectedItems.Clear();

        if (string.IsNullOrEmpty(_rootPath) || !Directory.Exists(_rootPath))
            return;

        try
        {
            if (ViewMode.Value == FileBrowserViewMode.Tree)
            {
                FileSystemEnumerator.PopulateCollection(TreeRootItems, _rootPath);
            }
            else
            {
                FileSystemEnumerator.PopulateCollection(Items, _rootPath);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogError(ex, "Access denied to directory {Path}", _rootPath);
            NotificationService.ShowWarning(Strings.FileBrowser, ex.Message);
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "IO error accessing directory {Path}", _rootPath);
            NotificationService.ShowWarning(Strings.FileBrowser, ex.Message);
        }
    }

    public void NavigateToBreadcrumb(int index)
    {
        if (index >= 0 && index < BreadcrumbItems.Count)
        {
            RootPath.Value = BreadcrumbItems[index].FullPath;
        }
    }

    private void UpdateBreadcrumbItems(string path)
    {
        BreadcrumbItems.Clear();

        if (string.IsNullOrEmpty(path))
            return;

        string? root = Path.GetPathRoot(path);
        if (root == null)
            return;

        // ルートセグメントを追加
        BreadcrumbItems.Add(new BreadcrumbPathItem(root, root));

        // ルート以降のセグメントを分割して追加
        string relativePart = path[root.Length..];
        string[] segments = relativePart.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        string accumulated = root;
        foreach (string segment in segments)
        {
            accumulated = Path.Combine(accumulated, segment);
            BreadcrumbItems.Add(new BreadcrumbPathItem(segment, accumulated));
        }
    }

    public void NavigateToHome()
    {
        ShowLocalFiles();
        IsHomeView.Value = true;
    }

    public void OpenItem(FileSystemItemViewModel item)
    {
        if (item.IsDirectory)
        {
            IsHomeView.Value = false;
            RootPath.Value = item.FullPath;
        }
        else
        {
            OpenFile(item.FullPath);
        }
    }

    public void Refresh()
    {
        if (IsHomeView.Value)
        {
            RefreshHomeView();
        }
        else
        {
            RefreshItems();
        }
    }

    private void RefreshHomeView()
    {
        _projectDirectory = EditorContextDirectories.GetProjectOrSceneDirectory(_editorContext);

        // お気に入りの更新
        _favoritesManager.RefreshFavoriteItems();

        // プロジェクトディレクトリの更新
        FileSystemEnumerator.DisposeAndClear(ProjectDirectoryItems);
        if (!string.IsNullOrEmpty(_projectDirectory) && Directory.Exists(_projectDirectory))
        {
            try
            {
                FileSystemEnumerator.PopulateCollection(ProjectDirectoryItems, _projectDirectory);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to enumerate project directory {Path}", _projectDirectory);
                NotificationService.ShowWarning(Strings.FileBrowser, ex.Message);
            }
        }

        // メディアファイル検索
        _mediaSearcher.SearchAsync(_projectDirectory);
    }

    public void ToggleFavorite(string path)
    {
        _favoritesManager.ToggleFavorite(path);
    }

    public void AddPathsToFavorites(IEnumerable<string> paths)
    {
        _favoritesManager.AddRange(paths);
    }

    public void WriteToJson(JsonObject json)
    {
        json["RootPath"] = RootPath.Value;
        json["ViewMode"] = (int)ViewMode.Value;
        json["IsHomeView"] = IsHomeView.Value;
        json["IsFavoritesIconView"] = IsFavoritesIconView.Value;
        json["IsProjectDirIconView"] = IsProjectDirIconView.Value;
        json["IsMediaFilesIconView"] = IsMediaFilesIconView.Value;
    }

    public void ReadFromJson(JsonObject json)
    {
        if (json.TryGetPropertyValue("RootPath", out var rootPathNode) && rootPathNode is JsonValue rootPathValue)
        {
            string? rootPath = rootPathValue.GetValue<string>();
            if (!string.IsNullOrEmpty(rootPath) && Directory.Exists(rootPath))
            {
                RootPath.Value = rootPath;
            }
        }

        if (json.TryGetPropertyValueAsJsonValue("ViewMode", out int viewModeInt)
            && Enum.IsDefined(typeof(FileBrowserViewMode), viewModeInt))
        {
            ViewMode.Value = (FileBrowserViewMode)viewModeInt;
        }

        if (json.TryGetPropertyValueAsJsonValue("IsHomeView", out bool isHome))
        {
            IsHomeView.Value = isHome;
        }

        if (json.TryGetPropertyValueAsJsonValue("IsFavoritesIconView", out bool favIcon))
        {
            IsFavoritesIconView.Value = favIcon;
        }

        if (json.TryGetPropertyValueAsJsonValue("IsProjectDirIconView", out bool projIcon))
        {
            IsProjectDirIconView.Value = projIcon;
        }

        if (json.TryGetPropertyValueAsJsonValue("IsMediaFilesIconView", out bool mediaIcon))
        {
            IsMediaFilesIconView.Value = mediaIcon;
        }
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseStorageBrowser();
        DecoderRegistry.DecodersChanged -= OnDecodersChanged;
        _mediaSearcher.Dispose();
        _favoritesManager.Dispose();
        _directoryWatcher.Dispose();

        FileSystemEnumerator.DisposeAndClear(Items);
        FileSystemEnumerator.DisposeAndClear(TreeRootItems);
        FileSystemEnumerator.DisposeAndClear(ProjectDirectoryItems);

        _disposables.Dispose();
    }
}
