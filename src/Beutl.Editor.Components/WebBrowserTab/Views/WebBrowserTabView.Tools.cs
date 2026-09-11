using System.Diagnostics;

using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;

using System.Collections.Specialized;

namespace Beutl.Editor.Components.WebBrowserTab.Views;

internal partial class WebBrowserTabView
{
    private Action? _closePanelAction;

    private void OnProfileChanged()
    {
        if (_viewModel is not { } vm) return;
        AddressTextBox.CancelSearchSuggestions();
        AddressTextBox.SuggestionsEnabled = vm.Profile.SuggestionsEnabled;
        AddressTextBox.SuggestionProvider = (query, token) => WebSearchSuggestions.Default.GetSuggestionsAsync(query, token, vm.Profile.Engine);
        SearchEngineLabel.Text = vm.Profile.Engine.ToString();
    }

    private void ShowToolStatus(string message)
    {
        DownloadStatusPanel.IsVisible = true;
        DownloadStatusText.Text = message;
    }

    private void CheckProfileSave(bool success)
    {
        if (!success) ShowToolStatus(string.Format(Strings.BrowserStorageError, _viewModel?.Profile.Error));
    }

    internal void ShowBrowserPanel(string title, Control content, Action? onClose = null)
    {
        CloseBrowserPanel();
        ToolPanelTitle.Text = title;
        ToolPanelContent.Content = content;
        _closePanelAction = onClose;
        BrowserSurface.IsVisible = false;
        ToolPanel.IsVisible = true;
    }

    internal void CloseBrowserPanel()
    {
        Action? close = _closePanelAction;
        _closePanelAction = null;
        ToolPanel.IsVisible = false;
        ToolPanelContent.Content = null;
        BrowserSurface.IsVisible = true;
        close?.Invoke();
    }

    private void OnCloseBrowserPanelClick(object? sender, RoutedEventArgs e) => CloseBrowserPanel();

    private void OnBookmarksChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateBookmarkEmptyState();

    private void UpdateBookmarkEmptyState() => BookmarkEmptyState.IsVisible = _viewModel?.Bookmarks.Count is null or 0;

    private void OnStartSearchClick(object? sender, RoutedEventArgs e)
    {
        AddressTextBox.Focus();
        AddressTextBox.SelectAll();
    }

    private void OnShowBookmarkEditorClick(object? sender, RoutedEventArgs e)
    {
        BookmarkEditor.IsVisible = true;
        BookmarkEditorError.Text = string.Empty;
        BookmarkUrlInput.Focus();
    }

    private void OnCancelBookmarkEditorClick(object? sender, RoutedEventArgs e)
    {
        BookmarkEditor.IsVisible = false;
        BookmarkUrlInput.Text = string.Empty;
        BookmarkNameInput.Text = string.Empty;
        BookmarkEditorError.Text = string.Empty;
    }

    private void OnSaveBookmarkClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not { } vm) return;
        if (!ViewModels.WebBrowserTabViewModel.TryNormalizeAddress(BookmarkUrlInput.Text, out Uri uri)
            || !BrowserProfile.IsAllowedUrl(uri.AbsoluteUri))
        {
            BookmarkEditorError.Text = Strings.InvalidWebAddress;
            return;
        }
        if (!vm.Profile.AddBookmark(uri, BookmarkNameInput.Text?.Trim() ?? string.Empty))
        {
            BookmarkEditorError.Text = string.Format(Strings.BrowserStorageError, vm.Profile.Error);
            return;
        }
        OnCancelBookmarkEditorClick(sender, e);
    }

    private void OnOpenBookmarkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BrowserBookmark item } && _viewModel is { } vm)
        {
            vm.Address.Value = item.Url;
            NavigateFromAddress();
        }
    }

    private void OnRemoveBookmarkClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: BrowserBookmark item } && _viewModel is { } vm)
            CheckProfileSave(vm.Profile.RemoveBookmark(item));
    }

    private ItemsControl? _historyItems;
    private Border? _historyEmpty;
    private TextBlock? _historyFeedback;

    private void OnDownloadHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshDownloadHistory();

    private void RefreshDownloadHistory()
    {
        if (_historyItems == null || _viewModel is not { } vm) return;
        _historyItems.ItemsSource = vm.Profile.Downloads.Select(record => new BrowserDownloadHistoryItem(record, vm.CanAddDownloadedMedia)).ToArray();
        _historyEmpty!.IsVisible = vm.Profile.Downloads.Count == 0;
    }

    private void OnDownloadHistoryClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not { } vm) return;
        var items = new ItemsControl { ItemTemplate = (IDataTemplate)Resources["DownloadHistoryItemTemplate"]! };
        var empty = new Border
        {
            Padding = new Avalonia.Thickness(20, 24),
            Child = new StackPanel { Spacing = 6, Children =
            {
                new TextBlock { Text = Strings.BrowserHistoryEmpty, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                new TextBlock { Text = Strings.BrowserHistoryEmptyHint, Opacity = 0.65, TextWrapping = Avalonia.Media.TextWrapping.Wrap }
            } }
        };
        var feedback = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap, IsVisible = false };
        var content = new StackPanel { Spacing = 12, Children =
        {
            new TextBlock { Text = Strings.BrowserHistoryIntro, Opacity = 0.65, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
            feedback, empty, items
        } };
        ShowBrowserPanel(Strings.BrowserDownloads, content, () =>
        {
            _historyItems = null;
            _historyEmpty = null;
            _historyFeedback = null;
        });
        _historyItems = items;
        _historyEmpty = empty;
        _historyFeedback = feedback;
        RefreshDownloadHistory();
    }

    private void WithHistoryRecord(object? sender, Action<BrowserDownloadRecord> action)
    {
        if (sender is not Button { Tag: BrowserDownloadRecord record }) return;
        try { action(record); }
        catch (Exception ex)
        {
            if (_historyFeedback != null)
            {
                _historyFeedback.Text = ex.Message;
                _historyFeedback.IsVisible = true;
            }
        }
    }

    private static void RequireHistoryFile(BrowserDownloadRecord record)
    {
        if (!File.Exists(record.FilePath)) throw new FileNotFoundException(Strings.BrowserFileMissing, record.FilePath);
    }

    private void OnOpenHistoryFileClick(object? sender, RoutedEventArgs e) => WithHistoryRecord(sender, record =>
    {
        RequireHistoryFile(record);
        Process.Start(new ProcessStartInfo(record.FilePath) { UseShellExecute = true });
    });

    private void OnOpenHistoryFolderClick(object? sender, RoutedEventArgs e) => WithHistoryRecord(sender, record =>
    {
        string directory = Path.GetDirectoryName(record.FilePath)!;
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException(Strings.BrowserFileMissing);
        Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
    });

    private async void OnImportHistoryClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: BrowserDownloadRecord record } || _viewModel is not { } vm) return;
        try
        {
            RequireHistoryFile(record);
            await vm.AddDownloadedMediaAsync(record.FilePath, CancellationToken.None);
        }
        catch (Exception ex)
        {
            if (!_disposed && ReferenceEquals(_viewModel, vm) && _historyFeedback != null)
            {
                _historyFeedback.Text = ex.Message;
                _historyFeedback.IsVisible = true;
            }
        }
    }

    private void OnRetryHistoryClick(object? sender, RoutedEventArgs e) => WithHistoryRecord(sender,
        record => { _ = DownloadMediaAsync(new Uri(record.Url), Path.GetFileName(record.FilePath)); });

    private void OnRemoveHistoryClick(object? sender, RoutedEventArgs e) => WithHistoryRecord(sender, record =>
    {
        if (_viewModel is { } vm) CheckProfileSave(vm.Profile.RemoveDownload(record));
    });

    private async void OnBrowserSettingsClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel?.GetService(typeof(IBrowserSettingsHost)) is not IBrowserSettingsHost host
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            ShowToolStatus(Strings.WebBrowserActionFailed);
            return;
        }

        try { await host.OpenBrowserSettingsAsync(owner); }
        catch (Exception ex) { if (!_disposed) ShowToolStatus(ex.Message); }
    }
}
