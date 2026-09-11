using System.Collections.Specialized;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;

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
        if (vm.Profile.Error != null) ShowToolStatus(string.Format(Strings.BrowserStorageError, vm.Profile.Error));
    }

    private void ShowToolStatus(string message)
    {
        ClearPageDownloadRequest();
        DownloadStatusPanel.IsVisible = true;
        DownloadStatusText.Text = message;
        ToolTip.SetTip(DownloadStatusText, null);
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

    private void OnBookmarksChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateBlankPageState();

    private void UpdateBlankPageState()
    {
        bool hasBookmarks = _viewModel?.Bookmarks.Count > 0;
        BrowserEmptyState.IsVisible = !hasBookmarks && !BookmarkEditor.IsVisible;
        BookmarkListPanel.IsVisible = hasBookmarks && !BookmarkEditor.IsVisible;
        BlankPageContent.VerticalAlignment = hasBookmarks && !BookmarkEditor.IsVisible
            ? Avalonia.Layout.VerticalAlignment.Top
            : Avalonia.Layout.VerticalAlignment.Center;
    }

    private void OnStartSearchClick(object? sender, RoutedEventArgs e)
    {
        AddressTextBox.Focus();
        AddressTextBox.SelectAll();
    }

    private void OnShowBookmarkEditorClick(object? sender, RoutedEventArgs e)
    {
        BookmarkEditor.IsVisible = true;
        BookmarkEditorError.Text = string.Empty;
        BookmarkEditorError.IsVisible = false;
        UpdateBlankPageState();
        BookmarkUrlInput.Focus();
    }

    private void OnCancelBookmarkEditorClick(object? sender, RoutedEventArgs e)
    {
        BookmarkEditor.IsVisible = false;
        BookmarkUrlInput.Text = string.Empty;
        BookmarkNameInput.Text = string.Empty;
        BookmarkEditorError.Text = string.Empty;
        BookmarkEditorError.IsVisible = false;
        UpdateBlankPageState();
    }

    private void OnSaveBookmarkClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not { } vm) return;
        if (!ViewModels.WebBrowserTabViewModel.TryNormalizeAddress(BookmarkUrlInput.Text, out Uri uri)
            || !BrowserProfile.IsAllowedUrl(uri.AbsoluteUri))
        {
            BookmarkEditorError.Text = Strings.InvalidWebAddress;
            BookmarkEditorError.IsVisible = true;
            return;
        }
        if (!vm.Profile.AddBookmark(uri, BookmarkNameInput.Text?.Trim() ?? string.Empty))
        {
            BookmarkEditorError.Text = string.Format(Strings.BrowserStorageError, vm.Profile.Error);
            BookmarkEditorError.IsVisible = true;
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
    private StackPanel? _historyContent;

    private void OnDownloadHistoryChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshDownloadHistory();

    private void RefreshDownloadHistory()
    {
        if (_historyItems == null || _viewModel is not { } vm) return;
        _historyItems.ItemsSource = vm.Profile.Downloads.Select(record => new BrowserDownloadHistoryItem(record, vm.CanAddDownloadedMedia)).ToArray();
        bool empty = vm.Profile.Downloads.Count == 0;
        _historyEmpty!.IsVisible = empty;
        _historyItems.IsVisible = !empty;
        _historyContent!.VerticalAlignment = empty ? Avalonia.Layout.VerticalAlignment.Center : Avalonia.Layout.VerticalAlignment.Top;
    }

    private void OnDownloadHistoryClick(object? sender, RoutedEventArgs e)
    {
        if (_viewModel is not { } vm) return;
        var items = new ItemsControl { ItemTemplate = (IDataTemplate)Resources["DownloadHistoryItemTemplate"]! };
        var empty = (Border)((IDataTemplate)Resources["DownloadHistoryEmptyTemplate"]!).Build(vm)!;
        var feedback = new TextBlock
        {
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            IsVisible = false,
            Classes = { "browserSecondary" }
        };
        var content = new StackPanel
        {
            MaxWidth = 600,
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
            Children = { feedback, empty, items }
        };
        var scroll = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = content
        };
        ShowBrowserPanel(Strings.BrowserDownloads, scroll, () =>
        {
            _historyItems = null;
            _historyEmpty = null;
            _historyFeedback = null;
            _historyContent = null;
        });
        _historyItems = items;
        _historyEmpty = empty;
        _historyFeedback = feedback;
        _historyContent = content;
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
        record =>
        {
            Uri.TryCreate(record.Referrer, UriKind.Absolute, out Uri? referrer);
            _ = DownloadMediaAsync(new Uri(record.Url), Path.GetFileName(record.FilePath), referrer);
        });

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
