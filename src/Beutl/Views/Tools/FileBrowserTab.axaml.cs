using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Beutl.ViewModels.Tools;
using FluentAvalonia.UI.Controls;
using SymbolIconSource = FluentIcons.FluentAvalonia.SymbolIconSource;

namespace Beutl.Views.Tools;

public partial class FileBrowserTab : UserControl
{
    private static readonly CrossFade s_transition = new(TimeSpan.FromMilliseconds(250));
    private CancellationTokenSource? _favoritesTransitionCts;
    private CancellationTokenSource? _projectDirTransitionCts;
    private CancellationTokenSource? _mediaFilesTransitionCts;

    public FileBrowserTab()
    {
        InitializeComponent();

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

    private FAMenuFlyout? FavoritesFlyout => FavoritesButton?.Flyout as FAMenuFlyout;

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

    private void OnToggleFavoriteClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null)
            return;

        ViewModel.ToggleFavorite();
        UpdateFavoritesFlyout();
    }

    private void UpdateFavoritesFlyout()
    {
        if (ViewModel == null || FavoritesFlyout == null)
            return;

        FavoritesFlyout.Items.Clear();

        if (ViewModel.Favorites.Count == 0)
        {
            var emptyItem = new MenuFlyoutItem
            {
                Text = Strings.NoFavorites,
                IsEnabled = false
            };
            FavoritesFlyout.Items.Add(emptyItem);
            return;
        }

        foreach (string path in ViewModel.Favorites)
        {
            var dirName = Path.GetFileName(path);
            if (string.IsNullOrEmpty(dirName))
            {
                dirName = path;
            }

            var menuItem = new MenuFlyoutItem
            {
                Text = dirName,
                Tag = path,
                IconSource = new SymbolIconSource { Symbol = FluentIcons.Common.Symbol.Folder }
            };
            ToolTip.SetTip(menuItem, path);
            menuItem.Click += OnFavoriteItemClick;
            FavoritesFlyout.Items.Add(menuItem);
        }
    }

    private void OnFavoriteItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: string path } && ViewModel != null)
        {
            ViewModel.NavigateToFavorite(path);
        }
    }

    private FileSystemItemViewModel? GetItemFromMenuItem(object? sender)
    {
        if (sender is MenuItem menuItem)
        {
            // ContextMenuの親要素からDataContextを取得
            var contextMenu = menuItem.GetLogicalAncestors().OfType<ContextMenu>().FirstOrDefault();
            if (contextMenu?.PlacementTarget?.DataContext is FileSystemItemViewModel item)
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

        flyout.ShowAt(sourceControl ?? this);
    }
}
