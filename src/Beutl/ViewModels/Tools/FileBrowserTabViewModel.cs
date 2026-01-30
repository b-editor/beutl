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

    public FileBrowserTabViewModel(EditViewModel editViewModel)
    {
        _editViewModel = editViewModel;

        // お気に入りをPreferencesから読み込み
        LoadFavorites();
        Favorites.CollectionChanged += OnFavoritesChanged;

        // シーンファイルの親ディレクトリを初期ルートとする
        if (editViewModel.Scene.Uri != null)
        {
            string? sceneDir = Path.GetDirectoryName(editViewModel.Scene.Uri.LocalPath);
            if (!string.IsNullOrEmpty(sceneDir))
            {
                RootPath.Value = sceneDir;
            }
        }

        RootPath.Subscribe(path =>
        {
            _rootPath = path;
            RefreshItems();
            SetupWatcher(path);
            IsFavorite.Value = Favorites.Contains(path);
        }).AddTo(_disposables);

        ViewMode.Subscribe(_ => RefreshItems()).AddTo(_disposables);
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
        Dispatcher.UIThread.Post(RefreshItems, DispatcherPriority.Background);
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshItems, DispatcherPriority.Background);
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
        RefreshItems();
    }

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
    }

    #endregion

    public void WriteToJson(JsonObject json)
    {
        json["RootPath"] = RootPath.Value;
        json["ViewMode"] = (int)ViewMode.Value;
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
    }

    public object? GetService(Type serviceType)
    {
        return null;
    }

    public void Dispose()
    {
        Favorites.CollectionChanged -= OnFavoritesChanged;
        _watcher?.Dispose();
        _watcher = null;

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
