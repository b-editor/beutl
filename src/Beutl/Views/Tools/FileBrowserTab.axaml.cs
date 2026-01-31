using System.IO;

using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;

using Beutl.Language;
using Beutl.ViewModels.Tools;

using FluentAvalonia.UI.Controls;

namespace Beutl.Views.Tools;

public partial class FileBrowserTab : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));
    private CancellationTokenSource? _favoritesTransitionCts;
    private CancellationTokenSource? _projectDirTransitionCts;
    private CancellationTokenSource? _mediaFilesTransitionCts;
    private Point? _dragStartPoint;
    private FileSystemItemViewModel? _dragItem;
    private bool _isDragStarting;

    public FileBrowserTab()
    {
        InitializeComponent();

        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);

        favoritesToggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                _favoritesTransitionCts?.Cancel();
                _favoritesTransitionCts = new CancellationTokenSource();
                var token = _favoritesTransitionCts.Token;
                if (v == true)
                    await s_transition.Start(null, favoritesContent, token);
                else
                    await s_transition.Start(favoritesContent, null, token);
            });

        projectDirToggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                _projectDirTransitionCts?.Cancel();
                _projectDirTransitionCts = new CancellationTokenSource();
                var token = _projectDirTransitionCts.Token;
                if (v == true)
                    await s_transition.Start(null, projectDirContent, token);
                else
                    await s_transition.Start(projectDirContent, null, token);
            });

        mediaFilesToggle.GetObservable(ToggleButton.IsCheckedProperty)
            .Subscribe(async v =>
            {
                _mediaFilesTransitionCts?.Cancel();
                _mediaFilesTransitionCts = new CancellationTokenSource();
                var token = _mediaFilesTransitionCts.Token;
                if (v == true)
                    await s_transition.Start(null, mediaFilesContent, token);
                else
                    await s_transition.Start(mediaFilesContent, null, token);
            });
    }

    private FileBrowserTabViewModel? ViewModel => DataContext as FileBrowserTabViewModel;

    private void OnNavigateUpClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.NavigateUp();
    }

    private void OnHomeClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.NavigateToHome();
    }

    private void OnViewModeListClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.ViewMode.Value = FileBrowserViewMode.List;
        }
    }

    private void OnViewModeTreeClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.ViewMode.Value = FileBrowserViewMode.Tree;
        }
    }

    private void OnViewModeIconClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.ViewMode.Value = FileBrowserViewMode.Icon;
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

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ViewModel == null || sender is not ListBox listBox)
            return;

        ViewModel.SelectedItems.Clear();
        foreach (var selectedItem in listBox.SelectedItems!)
        {
            if (selectedItem is FileSystemItemViewModel item)
            {
                ViewModel.SelectedItems.Add(item);
            }
        }

        // 後方互換: 先頭の選択アイテムをSelectedItemに設定
        ViewModel.SelectedItem.Value = ViewModel.SelectedItems.Count > 0
            ? ViewModel.SelectedItems[0]
            : null;
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
            contextMenu?.DataContext is FileSystemItemViewModel item &&
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
            ViewModel.ToggleFavoriteForPath(item.FullPath);
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

        var flyout = new RenameFlyout { Text = item.Name };
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

    private void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && sender is Control { DataContext: FileSystemItemViewModel item })
        {
            _dragStartPoint = e.GetPosition(this);
            _dragItem = item;
        }
    }

    private async void OnItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStartPoint == null || _dragItem == null || _isDragStarting)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragStartPoint = null;
            _dragItem = null;
            return;
        }

        Point currentPos = e.GetPosition(this);
        double dx = currentPos.X - _dragStartPoint.Value.X;
        double dy = currentPos.Y - _dragStartPoint.Value.Y;

        if (Math.Abs(dx) <= 5 && Math.Abs(dy) <= 5)
            return;

        if (TopLevel.GetTopLevel(this) is not { StorageProvider: IStorageProvider storageProvider })
            return;

        _isDragStarting = true;
        try
        {
            var data = new DataTransfer();

            // 選択中アイテムが複数あればすべて含める
            var items = ViewModel?.SelectedItems is { Count: > 1 } selectedItems
                && selectedItems.Contains(_dragItem)
                    ? selectedItems.ToList()
                    : new List<FileSystemItemViewModel> { _dragItem };

            foreach (FileSystemItemViewModel item in items)
            {
                IStorageItem? storageItem = item.IsDirectory
                    ? await storageProvider.TryGetFolderFromPathAsync(item.FullPath) as IStorageItem
                    : await storageProvider.TryGetFileFromPathAsync(item.FullPath);

                if (storageItem != null)
                {
                    data.Add(DataTransferItem.CreateFile(storageItem));
                }
            }

            await DragDrop.DoDragDropAsync(e, data, DragDropEffects.Copy);
        }
        finally
        {
            _dragStartPoint = null;
            _dragItem = null;
            _isDragStarting = false;
        }
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File) || ViewModel == null)
            return;

        e.DragEffects = DragDropEffects.Copy;

        string? targetDir = ViewModel.IsHomeView.Value
            ? ViewModel.ProjectDirectory
            : ViewModel.RootPath.Value;

        if (string.IsNullOrEmpty(targetDir) || !Directory.Exists(targetDir))
            return;

        foreach (IStorageItem src in e.DataTransfer.TryGetFiles() ?? [])
        {
            string? localPath = src.TryGetLocalPath();
            if (localPath == null)
                continue;

            string destPath = Path.Combine(targetDir, Path.GetFileName(localPath));

            if (src is IStorageFile)
            {
                if (!File.Exists(destPath))
                {
                    File.Copy(localPath, destPath);
                }
            }
            else if (src is IStorageFolder && Directory.Exists(localPath))
            {
                if (!Directory.Exists(destPath))
                {
                    CopyDirectoryRecursive(localPath, destPath);
                }
            }
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(destDir, Path.GetFileName(file));
            if (!File.Exists(destFile))
            {
                File.Copy(file, destFile);
            }
        }

        foreach (string dir in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectoryRecursive(dir, destSubDir);
        }
    }
}
