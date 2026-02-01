using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Avalonia.Data.Converters;
using Beutl.Logging;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.ViewModels.Tools;

public enum FileBrowserViewMode
{
    List,
    Tree,
    Icon
}

public sealed class FileBrowserTabViewModel : IToolContext
{
    private readonly CompositeDisposable _disposables = [];
    private readonly ILogger _logger = Log.CreateLogger<FileBrowserTabViewModel>();
    private readonly EditViewModel _editViewModel;
    private readonly DirectoryWatcherService _directoryWatcher = new();
    private string _rootPath = string.Empty;
    private readonly FavoritesManager _favoritesManager = new();
    private readonly MediaFileSearcher _mediaSearcher = new();
    private string? _projectDirectory;

    internal string? ProjectDirectory => _projectDirectory;

    public FileBrowserTabViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;

        // お気に入り変更時にホームビューを更新
        _favoritesManager.Changed += () =>
        {
            if (IsHomeView.Value)
            {
                _favoritesManager.RefreshFavoriteItems();
            }
        };

        // ディレクトリ変更時にリフレッシュ
        _directoryWatcher.Changed += () =>
        {
            if (IsHomeView.Value)
                RefreshHomeView();
            else
                RefreshItems();
        };

        // プロジェクトディレクトリの取得
        _projectDirectory = GetProjectDirectory();

        RootPath.Subscribe(path =>
        {
            _rootPath = path;
            if (!string.IsNullOrEmpty(path))
            {
                IsHomeView.Value = false;
            }
            UpdateBreadcrumbItems(path);
            RefreshItems();
            _directoryWatcher.Watch(path);
        }).AddTo(_disposables);

        IsHomeView.Subscribe(isHome =>
        {
            if (isHome)
            {
                RefreshHomeView();
                _directoryWatcher.Watch(_projectDirectory);
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
        _directoryWatcher.Watch(_projectDirectory);
    }

    public ToolTabExtension Extension => FileBrowserTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactiveProperty<bool>();

    public IReactiveProperty<ToolTabExtension.TabPlacement> Placement { get; } =
        new ReactiveProperty<ToolTabExtension.TabPlacement>(ToolTabExtension.TabPlacement.LeftLowerTop);

    public IReactiveProperty<ToolTabExtension.TabDisplayMode> DisplayMode { get; } =
        new ReactivePropertySlim<ToolTabExtension.TabDisplayMode>();

    public string Header => Strings.FileBrowser;

    public ReactiveProperty<FileBrowserViewMode> ViewMode { get; } = new(FileBrowserViewMode.Icon);

    public ReactiveProperty<string> RootPath { get; } = new(string.Empty);

    public ObservableCollection<BreadcrumbPathItem> BreadcrumbItems { get; } = [];

    public ReactivePropertySlim<bool> IsHomeView { get; } = new(true);

    // ファイル/フォルダの一覧（フラット表示用）
    public ObservableCollection<FileSystemItemViewModel> Items { get; } = [];

    // ツリー表示用のルートアイテム
    public ObservableCollection<FileSystemItemViewModel> TreeRootItems { get; } = [];

    public ObservableCollection<FileSystemItemViewModel> SelectedItems { get; } = [];

    public ObservableCollection<string> Favorites => _favoritesManager.Favorites;

    public ObservableCollection<FileSystemItemViewModel> FavoriteItems => _favoritesManager.FavoriteItems;

    public ObservableCollection<FileSystemItemViewModel> ProjectDirectoryItems { get; } = [];

    public ObservableCollection<FileSystemItemViewModel> MediaFileItems => _mediaSearcher.MediaFileItems;

    public ReactivePropertySlim<bool> IsLoadingMediaFiles => _mediaSearcher.IsLoadingMediaFiles;

    public ReactivePropertySlim<bool> HasNoMediaFiles => _mediaSearcher.HasNoMediaFiles;

    public ReactivePropertySlim<bool> IsFavoritesIconView { get; } = new(false);

    public ReactivePropertySlim<bool> IsProjectDirIconView { get; } = new(false);

    public ReactivePropertySlim<bool> IsMediaFilesIconView { get; } = new(true);

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

    private void RefreshItems()
    {
        DisposeAndClear(Items);
        DisposeAndClear(TreeRootItems);
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
            _logger.LogError(ex, "Failed to open file {Path}", path);
            NotificationService.ShowError(Strings.Open, Message.OperationCouldNotBeExecuted);
        }
    }

    public async Task DeleteItemAsync(FileSystemItemViewModel item)
    {
        var dialog = new ContentDialog
        {
            Title = Strings.Delete,
            Content = string.Format(Message.DoYouWantToDeleteThisFile, item.Name.Value),
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
                _logger.LogError(ex, "Failed to delete {Path}", item.FullPath);
                NotificationService.ShowError(Strings.Delete, Message.OperationCouldNotBeExecuted);
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
                    _logger.LogError(ex, "Failed to delete {Path}", item.FullPath);
                    NotificationService.ShowError(Strings.Delete, Message.OperationCouldNotBeExecuted);
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
            _logger.LogError(ex, "Failed to create folder at {Path}", newFolderPath);
            NotificationService.ShowError(Strings.NewFolder, Message.OperationCouldNotBeExecuted);
        }
    }

    public async Task RenameItemAsync(FileSystemItemViewModel item, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name.Value)
            return;

        string newPath = Path.Combine(Path.GetDirectoryName(item.FullPath)!, newName);

        if (File.Exists(newPath) || Directory.Exists(newPath))
        {
            var dialog = new ContentDialog
            {
                Title = Strings.Error,
                Content = string.Format(Message.CannotRenameBecauseConflicts, item.Name.Value, newName),
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
            _logger.LogError(ex, "Failed to rename {OldPath} to {NewPath}", item.FullPath, newPath);
            NotificationService.ShowError(Strings.Rename, Message.OperationCouldNotBeExecuted);
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
        _projectDirectory = GetProjectDirectory();

        // お気に入りの更新
        _favoritesManager.RefreshFavoriteItems();

        // プロジェクトディレクトリの更新
        DisposeAndClear(ProjectDirectoryItems);
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

    private static void DisposeAndClear(ObservableCollection<FileSystemItemViewModel> collection)
    {
        foreach (var item in collection)
        {
            item.Dispose();
        }
        collection.Clear();
    }

    public void ToggleFavorite(string path)
    {
        _favoritesManager.ToggleFavorite(path);
    }

    public void AddPathsToFavorites(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            if (!Favorites.Contains(path))
            {
                Favorites.Add(path);
            }
        }
    }

    public void CopyFilesToDirectory(IEnumerable<(string LocalPath, bool IsDirectory)> files, string targetDir)
    {
        foreach (var (localPath, isDir) in files)
        {
            string destPath = Path.Combine(targetDir, Path.GetFileName(localPath));

            try
            {
                if (!isDir)
                {
                    if (!File.Exists(destPath))
                    {
                        File.Copy(localPath, destPath);
                    }
                }
                else if (Directory.Exists(localPath))
                {
                    if (!Directory.Exists(destPath))
                    {
                        FileCopyService.CopyDirectoryRecursive(localPath, destPath);
                    }
                }
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Failed to copy {Source} to {Dest}", localPath, destPath);
                NotificationService.ShowError(Strings.Copy, Message.OperationCouldNotBeExecuted);
            }
        }
    }

    public void CopyFilesToResources(IEnumerable<(string LocalPath, bool IsDirectory)> files)
    {
        if (string.IsNullOrEmpty(_projectDirectory))
            return;

        string resourcesDir = Path.Combine(_projectDirectory, "resources");
        Directory.CreateDirectory(resourcesDir);
        CopyFilesToDirectory(files, resourcesDir);
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

        if (json.TryGetPropertyValue("ViewMode", out var viewModeNode)
            && viewModeNode is JsonValue viewModeValue
            && viewModeValue.TryGetValue(out int viewModeInt)
            && Enum.IsDefined(typeof(FileBrowserViewMode), viewModeInt))
        {
            ViewMode.Value = (FileBrowserViewMode)viewModeInt;
        }

        if (json.TryGetPropertyValue("IsHomeView", out var isHomeViewNode)
            && isHomeViewNode is JsonValue isHomeViewValue
            && isHomeViewValue.TryGetValue(out bool isHome))
        {
            IsHomeView.Value = isHome;
        }

        if (json.TryGetPropertyValue("IsFavoritesIconView", out var favIconNode)
            && favIconNode is JsonValue favIconVal
            && favIconVal.TryGetValue(out bool favIcon))
        {
            IsFavoritesIconView.Value = favIcon;
        }

        if (json.TryGetPropertyValue("IsProjectDirIconView", out var projIconNode)
            && projIconNode is JsonValue projIconVal
            && projIconVal.TryGetValue(out bool projIcon))
        {
            IsProjectDirIconView.Value = projIcon;
        }

        if (json.TryGetPropertyValue("IsMediaFilesIconView", out var mediaIconNode)
            && mediaIconNode is JsonValue mediaIconVal
            && mediaIconVal.TryGetValue(out bool mediaIcon))
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
        _mediaSearcher.Dispose();
        _favoritesManager.Dispose();
        _directoryWatcher.Dispose();

        DisposeAndClear(Items);
        DisposeAndClear(TreeRootItems);
        DisposeAndClear(ProjectDirectoryItems);

        _disposables.Dispose();
    }
}

public static class FileBrowserViewModeConverters
{
    public static FuncValueConverter<FileBrowserViewMode, bool> IsList { get; } =
        new(mode => mode == FileBrowserViewMode.List);

    public static FuncValueConverter<FileBrowserViewMode, bool> IsTree { get; } =
        new(mode => mode == FileBrowserViewMode.Tree);

    public static FuncValueConverter<FileBrowserViewMode, bool> IsIcon { get; } =
        new(mode => mode == FileBrowserViewMode.Icon);

    public static FuncValueConverter<FileBrowserViewMode, string> ToDisplayName { get; } =
        new(mode => mode switch
        {
            FileBrowserViewMode.List => Strings.ListView,
            FileBrowserViewMode.Tree => Strings.TreeView,
            FileBrowserViewMode.Icon => Strings.IconView,
            _ => string.Empty
        });
}
