using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Editor.Components.FileBrowserTab.Services;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Services;
using FluentAvalonia.UI.Controls;

namespace Beutl.Editor.Components.FileBrowserTab.Views;

public partial class FileBrowserTabView : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));
    private readonly Dictionary<Control, CancellationTokenSource> _sectionTransitionCts = [];

    public FileBrowserTabView()
    {
        InitializeComponent();
        StorageContent.ContentTemplate = new FuncDataTemplate<IFileBrowserStorageBrowser>(
            (browser, _) => browser?.CreateView());

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

    private void OnStorageProviderClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: IFileBrowserStorageProvider provider })
            ViewModel?.OpenStorage(provider);
    }

    private async void OnStorageBreadcrumbClicked(FABreadcrumbBar sender, FABreadcrumbBarItemClickedEventArgs e)
    {
        if (e.Item is FileBrowserStorageBreadcrumb breadcrumb && ViewModel?.StorageNavigation.Value is { } navigation)
            await navigation.NavigateToAsync(breadcrumb);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (this.FindControl<Button>("StorageLocationsButton") is not { } button) return;
        button.Flyout?.Hide();
        button.Flyout = null;
        if (ViewModel is not { } vm) return;

        var menu = new MenuFlyout();
        var local = new MenuItem { Header = Strings.LocalFiles };
        local.Click += (_, _) => vm.ShowLocalFiles();
        menu.Items.Add(local);
        menu.Items.Add(new Separator());
        foreach (var provider in vm.StorageProviders)
        {
            var item = new MenuItem { Header = provider.DisplayName, DataContext = provider };
            item.Click += OnStorageProviderClick;
            menu.Items.Add(item);
        }
        button.Flyout = menu;
    }

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

    private async void OnOpenInFileManagerClick(object? sender, RoutedEventArgs e)
    {
        if (GetItemFromMenuItem(sender) is { } item && ViewModel is { } vm)
            await vm.OpenInFileManagerAsync(item);
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

    private void OnContextFlyoutOpened(object? sender, EventArgs e)
    {
        if (sender is FAMenuFlyout contextMenu &&
            contextMenu.Target?.DataContext is FileSystemItemViewModel item &&
            ViewModel != null)
        {
            foreach (var menuItem in contextMenu.Items.OfType<FAMenuFlyoutItem>())
            {
                if (menuItem.Tag is "FavoriteToggle")
                {
                    bool isFavorite = ViewModel.Favorites.Contains(item.FullPath);
                    menuItem.Text = isFavorite ? Strings.RemoveFromFavorites : Strings.AddToFavorites;
                }
                else if (menuItem.Tag is "OpenInFileManager")
                {
                    menuItem.Text = FileManagerLauncher.MenuHeader;
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
        return (sender as FAMenuFlyoutItem)?.DataContext as FileSystemItemViewModel;
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
        flyout.ShowAt(target, true);
    }

    private void BreadcrumbBarItemClicked(FABreadcrumbBar sender, FABreadcrumbBarItemClickedEventArgs args)
    {
        ViewModel?.NavigateToBreadcrumb(args.Index);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (IsStaleStorageDrag(e))
        { e.DragEffects = DragDropEffects.None; return; }
        if (ViewModel?.IsStorageView.Value == true)
        {
            var breadcrumb = FindStorageBreadcrumb(e);
            e.DragEffects = breadcrumb != null && ViewModel.StorageBrowser.Value is IFileBrowserStorageDropTarget target && target.CanDrop(e.DataTransfer, breadcrumb.FolderId)
                ? DragDropEffects.Copy : DragDropEffects.None;
            return;
        }
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
            // 内部ドラッグでもソース側はCopyのみをアドバタイズしているため、ここではCopyを設定する。
            // 実際の移動判定はドロップ時に IsInternalDragInProgress を見て行う。
            e.DragEffects = DragDropEffects.Copy;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (IsStaleStorageDrag(e))
        { e.DragEffects = DragDropEffects.None; return; }
        if (ViewModel?.IsStorageView.Value == true)
        {
            var breadcrumb = FindStorageBreadcrumb(e);
            if (breadcrumb != null && ViewModel.StorageBrowser.Value is IFileBrowserStorageDropTarget target)
            {
                e.Handled = true;
                if (!target.CanDrop(e.DataTransfer, breadcrumb.FolderId)) { e.DragEffects = DragDropEffects.None; return; }
                try { await target.DropAsync(e.DataTransfer, breadcrumb.FolderId); }
                catch (OperationCanceledException) { }
                catch (Exception) { NotificationService.ShowError(Strings.CloudStorage, Strings.CloudStorageActionFailed); }
            }
            return;
        }
        if (!e.DataTransfer.Contains(DataFormat.File) || ViewModel == null)
            return;

        // The drop reads everything it needs from the event before the transfer starts, and the
        // transfer runs off the UI thread, so the editor keeps responding while the files are written.
        if (ViewModel.IsHomeView.Value)
        {
            await HandleHomeViewDrop(e);
        }
        else
        {
            await HandleBrowseViewDrop(e);
        }
    }

    private Task HandleHomeViewDrop(DragEventArgs e)
    {
        if (ViewModel == null)
            return Task.CompletedTask;

        var files = GetDroppedFiles(e);
        bool isInternal = FileItemDragBehavior.IsInternalDragInProgress;

        // お気に入りセクション上にドロップ → お気に入りに追加
        if (IsDropOverElement(favoritesSection, e))
        {
            ViewModel.AddPathsToFavorites(files.Select(f => f.LocalPath));
            return Task.CompletedTask;
        }

        if (IsDropOverElement(projectDirectorySection, e))
        {
            // その他（プロジェクトディレクトリセクション等）
            return TransferToDropTarget(e, files, isInternal, ViewModel.ProjectDirectory);
        }

        // メディアファイルセクション上にドロップ → resources フォルダへ
        var payload = files.Select(f => (f.LocalPath, f.IsDirectory));
        return isInternal
            ? ViewModel.MoveFilesToResourcesAsync(payload)
            : ViewModel.CopyFilesToResourcesAsync(payload);
    }

    private Task HandleBrowseViewDrop(DragEventArgs e)
    {
        if (ViewModel == null)
            return Task.CompletedTask;

        var files = GetDroppedFiles(e);
        bool isInternal = FileItemDragBehavior.IsInternalDragInProgress;

        return TransferToDropTarget(e, files, isInternal, ViewModel.RootPath.Value);
    }

    // A folder under the pointer takes the files; otherwise the directory being shown does, while it exists.
    private Task TransferToDropTarget(
        DragEventArgs e, List<(string LocalPath, bool IsDirectory)> files, bool isInternal, string? fallbackDirectory)
    {
        FileSystemItemViewModel? folderItem = FindFolderItemUnderCursor(e);
        if (folderItem != null && Directory.Exists(folderItem.FullPath))
        {
            return TransferFiles(files, folderItem.FullPath, isInternal);
        }

        if (!string.IsNullOrEmpty(fallbackDirectory) && Directory.Exists(fallbackDirectory))
        {
            return TransferFiles(files, fallbackDirectory, isInternal);
        }

        return Task.CompletedTask;
    }

    private Task TransferFiles(List<(string LocalPath, bool IsDirectory)> files, string targetDir, bool isInternal)
    {
        if (ViewModel == null)
            return Task.CompletedTask;

        var payload = files.Select(f => (f.LocalPath, f.IsDirectory));
        return isInternal
            ? ViewModel.MoveFilesToDirectoryAsync(payload, targetDir)
            : ViewModel.CopyFilesToDirectoryAsync(payload, targetDir);
    }

    private static bool IsStaleStorageDrag(DragEventArgs e)
        => e.DataTransfer.TryGetValue(StorageDragData.Format) is { } source && !source.IsCurrent();

    private static FileBrowserStorageBreadcrumb? FindStorageBreadcrumb(DragEventArgs e)
        => (e.Source as Visual)?.FindAncestorOfType<Control>(includeSelf: true)?.DataContext as FileBrowserStorageBreadcrumb;

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
