using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Beutl.ViewModels.Tools;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Tools;

public partial class FileBrowserTab : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));
    private readonly Dictionary<Control, CancellationTokenSource> _sectionTransitionCts = [];

    public FileBrowserTab()
    {
        InitializeComponent();

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);

        SetupSectionToggle(favoritesToggle, favoritesContent);
        SetupSectionToggle(projectDirToggle, projectDirContent);
        SetupSectionToggle(mediaFilesToggle, mediaFilesContent);
    }

    private void SetupSectionToggle(ToggleButton toggle, Control content)
    {
        toggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                if (_sectionTransitionCts.TryGetValue(content, out var oldCts))
                {
                    oldCts.Cancel();
                    oldCts.Dispose();
                }

                var cts = new CancellationTokenSource();
                _sectionTransitionCts[content] = cts;
                var token = cts.Token;
                if (v == true)
                    await s_transition.Start(null, content, token);
                else
                    await s_transition.Start(content, null, token);
            });
    }

    private FileBrowserTabViewModel? ViewModel => DataContext as FileBrowserTabViewModel;

    private async void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions());
        if (result.Count > 0 && result[0].TryGetLocalPath() is string localPath)
        {
            ViewModel.RootPath.Value = localPath;
        }
    }

    private void OnHomeClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.NavigateToHome();
    }

    private void OnCycleViewModeClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.ViewMode.Value = ViewModel.ViewMode.Value switch
            {
                FileBrowserViewMode.List => FileBrowserViewMode.Tree,
                FileBrowserViewMode.Tree => FileBrowserViewMode.Icon,
                FileBrowserViewMode.Icon => FileBrowserViewMode.List,
                _ => FileBrowserViewMode.List
            };
        }
    }

    private void OnNewFolderClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.CreateNewFolder();
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.Refresh();
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: FileSystemItemViewModel item } && ViewModel != null)
        {
            ViewModel.OpenItem(item);
            e.Handled = true;
        }
    }


    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        if (GetItemFromMenuItem(sender) is { } item && ViewModel != null)
        {
            ViewModel.OpenItem(item);
        }
    }

    private async void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null)
            return;

        // 複数選択されている場合は一括削除
        if (ViewModel.SelectedItems.Count > 1)
        {
            await ViewModel.DeleteItemsAsync(ViewModel.SelectedItems.ToList());
        }
        else if (GetItemFromMenuItem(sender) is { } item)
        {
            await ViewModel.DeleteItemAsync(item);
        }
    }

    private void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        if (GetItemFromMenuItem(sender) is { } item && ViewModel != null)
        {
            StartRename(item, sender as Control);
        }
    }

    private void OnContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu contextMenu &&
            contextMenu.DataContext is FileSystemItemViewModel item &&
            ViewModel != null)
        {
            foreach (var menuItem in contextMenu.Items.OfType<MenuItem>())
            {
                if (menuItem.Tag is "FavoriteToggle")
                {
                    bool isFavorite = ViewModel.Favorites.Contains(item.FullPath);
                    menuItem.Header = isFavorite ? Strings.RemoveFromFavorites : Strings.AddToFavorites;
                }
            }
        }
    }

    private void OnToggleFavoriteContextMenuClick(object? sender, RoutedEventArgs e)
    {
        if (GetItemFromMenuItem(sender) is { } item && ViewModel != null)
        {
            ViewModel.ToggleFavorite(item.FullPath);
        }
    }

    private FileSystemItemViewModel? GetItemFromMenuItem(object? sender)
    {
        if (sender is MenuItem menuItem)
        {
            // ContextMenuの親要素からDataContextを取得
            var contextMenu = menuItem.GetLogicalAncestors().OfType<ContextMenu>().FirstOrDefault();
            if (contextMenu?.DataContext is FileSystemItemViewModel item)
            {
                return item;
            }
        }

        return null;
    }

    private async void StartRename(FileSystemItemViewModel item, Control? sourceControl)
    {
        if (ViewModel == null) return;

        var flyout = new RenameFlyout { Text = item.Name.Value };
        flyout.Confirmed += async (_, newName) =>
        {
            if (!string.IsNullOrWhiteSpace(newName))
            {
                await ViewModel.RenameItemAsync(item, newName);
            }
        };

        var target = (Control?)sourceControl?.FindLogicalAncestorOfType<TreeViewItem>()
                     ?? (Control?)sourceControl?.FindLogicalAncestorOfType<ListBoxItem>()
                     ?? this;
        flyout.ShowAt(target);
    }

    private void BreadcrumbBarItemClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs args)
    {
        ViewModel?.NavigateToBreadcrumb(args.Index);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        if (ViewModel?.IsHomeView.Value == true && IsDropOverElement(favoritesSection, e))
        {
            e.DragEffects = DragDropEffects.Link;
        }
        else
        {
            e.DragEffects = DragDropEffects.Copy;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File) || ViewModel == null)
            return;

        if (ViewModel.IsHomeView.Value)
        {
            HandleHomeViewDrop(e);
        }
        else
        {
            HandleBrowseViewDrop(e);
        }
    }

    private void HandleHomeViewDrop(DragEventArgs e)
    {
        if (ViewModel == null)
            return;

        var files = GetDroppedFiles(e);

        // お気に入りセクション上にドロップ → お気に入りに追加
        if (IsDropOverElement(favoritesSection, e))
        {
            ViewModel.AddPathsToFavorites(files.Select(f => f.LocalPath));
            return;
        }

        // メディアファイルセクション上にドロップ → resources フォルダにコピー
        if (IsDropOverElement(mediaFilesSection, e))
        {
            ViewModel.CopyFilesToResources(files.Select(f => (f.LocalPath, f.IsDirectory)));
            return;
        }

        // その他（プロジェクトディレクトリセクション等）
        FileSystemItemViewModel? folderItem = FindFolderItemUnderCursor(e);
        if (folderItem != null && Directory.Exists(folderItem.FullPath))
        {
            ViewModel.CopyFilesToDirectory(files.Select(f => (f.LocalPath, f.IsDirectory)), folderItem.FullPath);
            return;
        }

        string? targetDir = ViewModel.ProjectDirectory;
        if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
        {
            ViewModel.CopyFilesToDirectory(files.Select(f => (f.LocalPath, f.IsDirectory)), targetDir);
        }
    }

    private void HandleBrowseViewDrop(DragEventArgs e)
    {
        if (ViewModel == null)
            return;

        var files = GetDroppedFiles(e);

        FileSystemItemViewModel? folderItem = FindFolderItemUnderCursor(e);
        if (folderItem != null && Directory.Exists(folderItem.FullPath))
        {
            ViewModel.CopyFilesToDirectory(files.Select(f => (f.LocalPath, f.IsDirectory)), folderItem.FullPath);
            return;
        }

        string? rootPath = ViewModel.RootPath.Value;
        if (!string.IsNullOrEmpty(rootPath) && Directory.Exists(rootPath))
        {
            ViewModel.CopyFilesToDirectory(files.Select(f => (f.LocalPath, f.IsDirectory)), rootPath);
        }
    }

    private static bool IsDropOverElement(Control element, DragEventArgs e)
    {
        if (!element.IsEffectivelyVisible)
            return false;

        var pos = e.GetPosition(element);
        return new Rect(element.Bounds.Size).Contains(pos);
    }

    private static FileSystemItemViewModel? FindFolderItemUnderCursor(DragEventArgs e)
    {
        var source = e.Source as Control;
        while (source != null)
        {
            if ((source is ListBoxItem or TreeViewItem) &&
                source.DataContext is FileSystemItemViewModel { IsDirectory: true } folderVm)
            {
                return folderVm;
            }

            source = source.Parent as Control;
        }

        return null;
    }

    private static List<(string LocalPath, bool IsDirectory)> GetDroppedFiles(DragEventArgs e)
    {
        var result = new List<(string, bool)>();
        foreach (IStorageItem src in e.DataTransfer.TryGetFiles() ?? [])
        {
            string? localPath = src.TryGetLocalPath();
            if (localPath != null)
            {
                result.Add((localPath, src is IStorageFolder));
            }
        }

        return result;
    }
}
