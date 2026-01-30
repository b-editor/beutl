using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Text.Json;
using System.Text.Json.Nodes;
using Avalonia.Data.Converters;
using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Tools;

/// <summary>
/// ファイルブラウザの表示モード
/// </summary>
public enum FileBrowserViewMode
{
    List,
    Tree,
    Icon
}

/// <summary>
/// ファイルブラウザToolTabのViewModel
/// </summary>
public sealed class FileBrowserTabViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly ILogger _logger = Log.CreateLogger<FileBrowserTabViewModel>();
    private readonly EditViewModel _editViewModel;
    private FileSystemWatcher? _watcher;
    private string _rootPath = string.Empty;
    private CancellationTokenSource? _mediaSearchCts;
    private string? _projectDirectory;

    private static readonly HashSet<string> s_mediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif",
        ".mp4", ".avi", ".mov", ".mkv", ".wmv", ".flv", ".webm",
        ".mp3", ".wav", ".ogg", ".flac", ".aac", ".wma", ".m4a"
    };

    public FileBrowserTabViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;

        // お気に入りをPreferencesから読み込み
        LoadFavorites();
        Favorites.CollectionChanged += OnFavoritesChanged;

        // プロジェクトディレクトリの取得
        _projectDirectory = GetProjectDirectory();

        RootPath.Subscribe(path =>
        {
            _rootPath = path;
            if (!string.IsNullOrEmpty(path))
            {
                IsHomeView.Value = false;
            }
            RefreshItems();
            SetupWatcher(path);
            IsFavorite.Value = Favorites.Contains(path);
        }).AddTo(_disposables);

        IsHomeView.Subscribe(isHome =>
        {
            if (isHome)
            {
                RefreshHomeView();
            }
        }).AddTo(_disposables);

        ViewMode.Subscribe(_ =>
        {
            if (!IsHomeView.Value)
            {
                RefreshItems();
            }
        }).AddTo(_disposables);

        // 初期化: ホームビューで起動（RootPathは設定しない）
        RefreshHomeView();
    }

    public ToolTabExtension Extension => FileBrowserTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; } =
        new ReactiveProperty<ToolTabExtension.TabPlacement>(ToolTabExtension.TabPlacement.LeftLowerTop);

    public IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabDisplayMode>();

    public string Header => Strings.FileBrowser;

    /// <summary>
    /// 表示モード（リスト/ツリー/アイコン）
    /// </summary>
    public ReactiveProperty<FileBrowserViewMode> ViewMode { get; } = new(FileBrowserViewMode.Icon);

    /// <summary>
    /// ルートディレクトリパス
    /// </summary>
    public ReactiveProperty<string> RootPath { get; } = new(string.Empty);

    /// <summary>
    /// ホームビュー表示中かどうか
    /// </summary>
    public ReactivePropertySlim<bool> IsHomeView { get; } = new(true);

    /// <summary>
    /// ファイル/フォルダの一覧（フラット表示用）
    /// </summary>
    public ObservableCollection<FileSystemItemViewModel> Items { get; } = [];

    /// <summary>
    /// ツリー表示用のルートアイテム
    /// </summary>
    public ObservableCollection<FileSystemItemViewModel> TreeRootItems { get; } = [];

    /// <summary>
    /// 選択中のアイテム（単一選択の後方互換）
    /// </summary>
    public ReactiveProperty<FileSystemItemViewModel?> SelectedItem { get; } = new();

    /// <summary>
    /// 選択中のアイテム（複数選択対応）
    /// </summary>
    public ObservableCollection<FileSystemItemViewModel> SelectedItems { get; } = [];

    /// <summary>
    /// お気に入りディレクトリのリスト
    /// </summary>
    public ObservableCollection<string> Favorites { get; } = [];

    /// <summary>
    /// 現在のディレクトリがお気に入りに含まれるか
    /// </summary>
    public ReactiveProperty<bool> IsFavorite { get; } = new(false);

    /// <summary>
    /// ホームビュー: お気に入りアイテム
    /// </summary>
    public ObservableCollection<FileSystemItemViewModel> FavoriteItems { get; } = [];

    /// <summary>
    /// ホームビュー: プロジェクトディレクトリアイテム
    /// </summary>
    public ObservableCollection<FileSystemItemViewModel> ProjectDirectoryItems { get; } = [];

    /// <summary>
    /// ホームビュー: メディアファイルアイテム
    /// </summary>
    public ObservableCollection<FileSystemItemViewModel> MediaFileItems { get; } = [];

    /// <summary>
    /// メディアファイル検索中かどうか
    /// </summary>
    public ReactivePropertySlim<bool> IsLoadingMediaFiles { get; } = new(false);

    /// <summary>
    /// メディアファイルが見つからなかったか
    /// </summary>
    public ReactivePropertySlim<bool> HasNoMediaFiles { get; } = new(false);

    private string? GetProjectDirectory()
    {
        var project = ProjectService.Current.CurrentProject.Value;
        if (project?.Uri != null)
            return Path.GetDirectoryName(project.Uri.LocalPath);

        // フォールバック: シーンディレクトリの1つ上
        if (_editViewModel.Scene.Uri != null)
        {
            string? sceneDir = Path.GetDirectoryName(_editViewModel.Scene.Uri.LocalPath);
            if (!string.IsNullOrEmpty(sceneDir))
                return Path.GetDirectoryName(sceneDir);
        }
        return null;
    }

    private void SetupWatcher(string path)
    {
        _watcher?.Dispose();

        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
            return;

        try
        {
            _watcher = new FileSystemWatcher(path)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };

            _watcher.Created += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemRenamed;
            _watcher.Changed += OnFileSystemChanged;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create FileSystemWatcher for {Path}", path);
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (IsHomeView.Value)
                RefreshHomeView();
            else
                RefreshItems();
        }, DispatcherPriority.Background);
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (IsHomeView.Value)
                RefreshHomeView();
            else
                RefreshItems();
        }, DispatcherPriority.Background);
    }

    private void RefreshItems()
    {
        foreach (var item in Items)
        {
            item.Dispose();
        }
        Items.Clear();

        foreach (var item in TreeRootItems)
        {
            item.Dispose();
        }
        TreeRootItems.Clear();

        SelectedItems.Clear();

        if (string.IsNullOrEmpty(_rootPath) || !Directory.Exists(_rootPath))
            return;

        try
        {
            var dirInfo = new DirectoryInfo(_rootPath);

            // Directories first
            foreach (var dir in dirInfo.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            {
                if ((dir.Attributes & FileAttributes.Hidden) == 0)
                {
                    var item = new FileSystemItemViewModel(dir.FullName, true);
                    Items.Add(item);
                    TreeRootItems.Add(new FileSystemItemViewModel(dir.FullName, true));
                }
            }

            // Then files
            foreach (var file in dirInfo.GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                if ((file.Attributes & FileAttributes.Hidden) == 0)
                {
                    var item = new FileSystemItemViewModel(file.FullName, false);
                    Items.Add(item);
                    TreeRootItems.Add(new FileSystemItemViewModel(file.FullName, false));
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Access denied to directory {Path}", _rootPath);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "IO error accessing directory {Path}", _rootPath);
        }
    }

    public void NavigateUp()
    {
        if (string.IsNullOrEmpty(_rootPath))
            return;

        var parent = Directory.GetParent(_rootPath);
        if (parent != null)
        {
            RootPath.Value = parent.FullName;
        }
    }

    public void NavigateTo(string path)
    {
        if (Directory.Exists(path))
        {
            RootPath.Value = path;
        }
    }

    public void NavigateToHome()
    {
        IsHomeView.Value = true;
    }

    public void OpenItem(FileSystemItemViewModel item)
    {
        if (item.IsDirectory)
        {
            RootPath.Value = item.FullPath;
        }
        else
        {
            OpenFile(item.FullPath);
        }
    }

    public void OpenFile(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open file {Path}", path);
        }
    }

    public async Task DeleteItemAsync(FileSystemItemViewModel item)
    {
        var dialog = new ContentDialog
        {
            Title = Strings.Delete,
            Content = string.Format(Message.DoYouWantToDeleteThisFile, item.Name),
            PrimaryButtonText = Strings.Yes,
            CloseButtonText = Strings.No,
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                if (item.IsDirectory)
                {
                    Directory.Delete(item.FullPath, true);
                }
                else
                {
                    File.Delete(item.FullPath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete {Path}", item.FullPath);
            }
        }
    }

    public async Task DeleteItemsAsync(IReadOnlyList<FileSystemItemViewModel> items)
    {
        if (items.Count == 0)
            return;

        if (items.Count == 1)
        {
            await DeleteItemAsync(items[0]);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = Strings.Delete,
            Content = string.Format(Strings.DeleteSelectedItems, items.Count),
            PrimaryButtonText = Strings.Yes,
            CloseButtonText = Strings.No,
            DefaultButton = ContentDialogButton.Close
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            foreach (var item in items)
            {
                try
                {
                    if (item.IsDirectory)
                    {
                        Directory.Delete(item.FullPath, true);
                    }
                    else
                    {
                        File.Delete(item.FullPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete {Path}", item.FullPath);
                }
            }
        }
    }

    public void CreateNewFolder()
    {
        if (string.IsNullOrEmpty(_rootPath) || !Directory.Exists(_rootPath))
            return;

        string baseName = Strings.NewFolder;
        string newFolderPath = Path.Combine(_rootPath, baseName);
        int counter = 1;

        while (Directory.Exists(newFolderPath))
        {
            newFolderPath = Path.Combine(_rootPath, $"{baseName} ({counter})");
            counter++;
        }

        try
        {
            Directory.CreateDirectory(newFolderPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create folder at {Path}", newFolderPath);
        }
    }

    public async Task RenameItemAsync(FileSystemItemViewModel item, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name)
            return;

        string newPath = Path.Combine(Path.GetDirectoryName(item.FullPath)!, newName);

        if (File.Exists(newPath) || Directory.Exists(newPath))
        {
            var dialog = new ContentDialog
            {
                Title = Strings.Error,
                Content = string.Format(Message.CannotRenameBecauseConflicts, item.Name, newName),
                CloseButtonText = Strings.Close
            };
            await dialog.ShowAsync();
            return;
        }

        try
        {
            if (item.IsDirectory)
            {
                Directory.Move(item.FullPath, newPath);
            }
            else
            {
                File.Move(item.FullPath, newPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to rename {OldPath} to {NewPath}", item.FullPath, newPath);
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

    #region HomeView

    private void RefreshHomeView()
    {
        _projectDirectory = GetProjectDirectory();

        // お気に入りの更新
        DisposeAndClear(FavoriteItems);
        foreach (string path in Favorites)
        {
            if (Directory.Exists(path))
            {
                FavoriteItems.Add(new FileSystemItemViewModel(path, true));
            }
            else if (File.Exists(path))
            {
                FavoriteItems.Add(new FileSystemItemViewModel(path, false));
            }
        }

        // プロジェクトディレクトリの更新
        DisposeAndClear(ProjectDirectoryItems);
        if (!string.IsNullOrEmpty(_projectDirectory) && Directory.Exists(_projectDirectory))
        {
            try
            {
                var dirInfo = new DirectoryInfo(_projectDirectory);

                foreach (var dir in dirInfo.GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if ((dir.Attributes & FileAttributes.Hidden) == 0)
                    {
                        ProjectDirectoryItems.Add(new FileSystemItemViewModel(dir.FullName, true));
                    }
                }

                foreach (var file in dirInfo.GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                {
                    if ((file.Attributes & FileAttributes.Hidden) == 0)
                    {
                        ProjectDirectoryItems.Add(new FileSystemItemViewModel(file.FullName, false));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enumerate project directory {Path}", _projectDirectory);
            }
        }

        // メディアファイル検索
        SearchMediaFilesAsync();
    }

    private void SearchMediaFilesAsync()
    {
        _mediaSearchCts?.Cancel();
        _mediaSearchCts = new CancellationTokenSource();
        var token = _mediaSearchCts.Token;

        DisposeAndClear(MediaFileItems);
        IsLoadingMediaFiles.Value = true;
        HasNoMediaFiles.Value = false;

        string? searchDir = _projectDirectory;
        if (string.IsNullOrEmpty(searchDir) || !Directory.Exists(searchDir))
        {
            IsLoadingMediaFiles.Value = false;
            HasNoMediaFiles.Value = true;
            return;
        }

        Task.Run(() =>
        {
            var results = new List<string>();
            try
            {
                SearchMediaFilesRecursive(searchDir, results, token, maxCount: 200);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested)
                return;

            Dispatcher.UIThread.Post(() =>
            {
                if (token.IsCancellationRequested)
                    return;

                foreach (string filePath in results)
                {
                    MediaFileItems.Add(new FileSystemItemViewModel(filePath, false));
                }

                IsLoadingMediaFiles.Value = false;
                HasNoMediaFiles.Value = MediaFileItems.Count == 0;
            });
        }, token);
    }

    private static void SearchMediaFilesRecursive(string directory, List<string> results, CancellationToken token, int maxCount)
    {
        token.ThrowIfCancellationRequested();

        try
        {
            foreach (string file in Directory.GetFiles(directory))
            {
                token.ThrowIfCancellationRequested();
                if (results.Count >= maxCount) return;

                string ext = Path.GetExtension(file);
                if (s_mediaExtensions.Contains(ext))
                {
                    results.Add(file);
                }
            }

            foreach (string subDir in Directory.GetDirectories(directory))
            {
                token.ThrowIfCancellationRequested();
                if (results.Count >= maxCount) return;

                try
                {
                    var dirInfo = new DirectoryInfo(subDir);
                    if ((dirInfo.Attributes & FileAttributes.Hidden) == 0)
                    {
                        SearchMediaFilesRecursive(subDir, results, token, maxCount);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // skip inaccessible directories
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            // skip inaccessible directories
        }
    }

    private static void DisposeAndClear(ObservableCollection<FileSystemItemViewModel> collection)
    {
        foreach (var item in collection)
        {
            item.Dispose();
        }
        collection.Clear();
    }

    #endregion

    #region Favorites

    public void ToggleFavorite()
    {
        if (string.IsNullOrEmpty(_rootPath))
            return;

        if (Favorites.Contains(_rootPath))
        {
            Favorites.Remove(_rootPath);
            IsFavorite.Value = false;
        }
        else
        {
            Favorites.Add(_rootPath);
            IsFavorite.Value = true;
        }
    }

    public void RemoveFavorite(string path)
    {
        Favorites.Remove(path);
        if (_rootPath == path)
        {
            IsFavorite.Value = false;
        }
    }

    public void NavigateToFavorite(string path)
    {
        if (Directory.Exists(path))
        {
            RootPath.Value = path;
        }
        else
        {
            // 存在しないお気に入りを自動削除
            Favorites.Remove(path);
        }
    }

    private void LoadFavorites()
    {
        try
        {
            string json = Preferences.Default.Get("FileBrowser.Favorites", "[]");
            var paths = JsonSerializer.Deserialize<string[]>(json);
            if (paths != null)
            {
                foreach (var p in paths)
                {
                    Favorites.Add(p);
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    private void SaveFavorites()
    {
        try
        {
            Preferences.Default.Set("FileBrowser.Favorites", JsonSerializer.Serialize(Favorites.ToArray()));
        }
        catch
        {
            // ignore
        }
    }

    private void OnFavoritesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        SaveFavorites();

        // ホームビュー表示中ならお気に入りセクションを更新
        if (IsHomeView.Value)
        {
            DisposeAndClear(FavoriteItems);
            foreach (string path in Favorites)
            {
                if (Directory.Exists(path))
                {
                    FavoriteItems.Add(new FileSystemItemViewModel(path, true));
                }
                else if (File.Exists(path))
                {
                    FavoriteItems.Add(new FileSystemItemViewModel(path, false));
                }
            }
        }
    }

    #endregion

    public void WriteToJson(JsonObject json)
    {
        json["RootPath"] = RootPath.Value;
        json["ViewMode"] = (int)ViewMode.Value;
        json["IsHomeView"] = IsHomeView.Value;
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

        if (json.TryGetPropertyValue("ViewMode", out var viewModeNode) && viewModeNode is JsonValue viewModeValue)
        {
            if (viewModeValue.TryGetValue(out int viewModeInt) && Enum.IsDefined(typeof(FileBrowserViewMode), viewModeInt))
            {
                ViewMode.Value = (FileBrowserViewMode)viewModeInt;
            }
        }

        if (json.TryGetPropertyValue("IsHomeView", out var isHomeViewNode) && isHomeViewNode is JsonValue isHomeViewValue)
        {
            if (isHomeViewValue.TryGetValue(out bool isHome))
            {
                IsHomeView.Value = isHome;
            }
        }
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }

    public void Dispose()
    {
        _mediaSearchCts?.Cancel();
        _mediaSearchCts?.Dispose();

        Favorites.CollectionChanged -= OnFavoritesChanged;
        _watcher?.Dispose();
        _watcher = null;

        DisposeAndClear(Items);
        DisposeAndClear(TreeRootItems);
        DisposeAndClear(FavoriteItems);
        DisposeAndClear(ProjectDirectoryItems);
        DisposeAndClear(MediaFileItems);

        _disposables.Dispose();
    }
}

/// <summary>
/// FileBrowserViewModeのコンバーター
/// </summary>
public static class FileBrowserViewModeConverters
{
    public static FuncValueConverter<FileBrowserViewMode, bool> IsList { get; } =
        new(mode => mode == FileBrowserViewMode.List);

    public static FuncValueConverter<FileBrowserViewMode, bool> IsTree { get; } =
        new(mode => mode == FileBrowserViewMode.Tree);

    public static FuncValueConverter<FileBrowserViewMode, bool> IsIcon { get; } =
        new(mode => mode == FileBrowserViewMode.Icon);
}
