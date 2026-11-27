using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Agents;
using Beutl.Configuration;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.Editor.Components.Helpers;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentComposerView : UserControl
{
    private const double CompactPermissionThreshold = 330;
    private const double CompactPermissionThresholdWithScene = 420;
    private AgentTabViewModel? _viewModel;
    private bool _attached;
    private bool _suggestionsRefreshQueued;
    private AgentComposerToken? _suggestionToken;
    private AgentComposerToken? _dismissedToken;

    public AgentComposerView()
    {
        InitializeComponent();
        PromptBox.AddHandler(KeyDownEvent, OnPromptKeyDown, RoutingStrategies.Tunnel);
        PromptBox.PastingFromClipboard += OnPromptPasting;
        PromptBox.PropertyChanged += OnPromptBoxPropertyChanged;
        PromptBox.GotFocus += (_, _) => QueueSuggestionsRefresh();
        PromptBox.LostFocus += (_, _) => EndSuggestions();
        SuggestionsList.AddHandler(PointerReleasedEvent, OnSuggestionPointerReleased, RoutingStrategies.Tunnel);
        ComposerCard.PropertyChanged += OnComposerCardPropertyChanged;
        ComposerCard.AddHandler(DragDrop.DragEnterEvent, OnDragOver);
        ComposerCard.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        ComposerCard.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        ComposerCard.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    public bool FocusPrompt()
    {
        if (!PromptBox.Focus() && !PromptBox.IsFocused)
            return false;
        PromptBox.CaretIndex = PromptBox.Text?.Length ?? 0;
        return true;
    }

    internal bool HasOpenSuggestions => SuggestionsPopup.IsOpen;
    internal bool IsModelPickerOpen => ModelButton.Flyout?.IsOpen == true;
    internal void DismissSuggestions() => HandleSuggestionKey(Key.Escape);

    internal void OpenModelPicker()
    {
        if (IsModelPickerOpen)
            ModelPicker.FocusSearch();
        else
            ModelButton.Flyout?.ShowAt(ModelButton);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_attached)
            AttachViewModel(DataContext as AgentTabViewModel);
        UpdatePermissionCompact();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        AttachViewModel(DataContext as AgentTabViewModel);
        UpdatePermissionCompact();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        EndSuggestions();
        ComposerCard.Classes.Set("dragover", false);
        AttachViewModel(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void AttachViewModel(AgentTabViewModel? viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
            return;
        CloseSuggestions();
        if (_viewModel is not null)
        {
            _viewModel.ResetMentionCandidates();
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.ComposerSuggestionsChanged -= QueueSuggestionsRefresh;
        }
        _viewModel = viewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.ComposerSuggestionsChanged += QueueSuggestionsRefresh;
        }
        _dismissedToken = null;
        QueueSuggestionsRefresh();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AgentTabViewModel.IncludeSceneContext) or null or "")
            UpdatePermissionCompact();
        if (e.PropertyName is nameof(AgentTabViewModel.CanManage) or nameof(AgentTabViewModel.ShowComposer) or null or "")
        {
            if (_viewModel is not { CanManage: true, ShowComposer: true })
                ComposerCard.Classes.Set("dragover", false);
            QueueSuggestionsRefresh();
        }
    }

    private void OnComposerCardPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty)
        {
            UpdatePermissionCompact();
            QueueSuggestionsRefresh();
        }
    }

    private void UpdatePermissionCompact()
    {
        double width = ComposerCard.Bounds.Width;
        double threshold = (DataContext as AgentTabViewModel)?.IncludeSceneContext == true
            ? CompactPermissionThresholdWithScene
            : CompactPermissionThreshold;
        PermissionPicker.IsCompact = width > 0 && width < threshold;
    }

    private void OnRemoveSceneContext(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AgentTabViewModel { CanManage: true } viewModel)
            viewModel.IncludeSceneContext = false;
    }

    private async void OnPromptKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not AgentTabViewModel viewModel || e.Key == Key.ImeProcessed || HasActiveComposition())
            return;

        KeyModifiers command = KeyGestureHelper.GetCommandModifier();
        bool commandPressed = e.KeyModifiers == command;
        bool alternateModifiersPressed = e.KeyModifiers == (command | KeyModifiers.Shift);
        if (SuggestionsPopup.IsOpen
            && (e.KeyModifiers == KeyModifiers.None || (e.Key == Key.Enter && (commandPressed || alternateModifiersPressed)))
            && HandleSuggestionKey(e.Key))
        {
            e.Handled = true;
            return;
        }
        if (e.KeyModifiers == KeyModifiers.None && (e.Key is Key.Up or Key.Down) && PromptBox.SelectionStart == PromptBox.SelectionEnd)
        {
            bool previous = e.Key == Key.Up;
            if (viewModel.RecallPrompt(previous ? -1 : 1, IsAtHistoryBoundary(previous)))
            {
                e.Handled = true;
                int caret = previous ? 0 : PromptBox.Text?.Length ?? 0;
                PromptBox.CaretIndex = PromptBox.SelectionStart = PromptBox.SelectionEnd = caret;
            }
            return;
        }
        if (e.Key != Key.Enter)
            return;

        bool sendWithCommand = viewModel.ComposerSendKey == AgentComposerSendKey.CtrlEnter;
        bool alternate = viewModel.IsRunning && (sendWithCommand ? alternateModifiersPressed : commandPressed);
        bool send = commandPressed || (!sendWithCommand && e.KeyModifiers == KeyModifiers.None);
        if (!alternate && !send)
            return;

        e.Handled = true;
        if (alternate)
            await viewModel.SendAlternateAsync();
        else
            await viewModel.SendAsync();
    }

    private TextPresenter? PromptPresenter => PromptBox.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();

    private bool HasActiveComposition() => !string.IsNullOrEmpty(PromptPresenter?.PreeditText);

    private bool IsAtHistoryBoundary(bool previous)
    {
        if (PromptPresenter is not { } presenter)
            return true;
        var layout = presenter.TextLayout;
        int line = layout.GetLineIndexFromCharacterIndex(PromptBox.CaretIndex, trailingEdge: false);
        return previous ? line <= 0 : line == layout.TextLines.Count - 1;
    }

    private void OnPromptBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextBox.TextProperty)
            _dismissedToken = null;
        if (e.Property == TextBox.TextProperty || e.Property == TextBox.CaretIndexProperty
            || e.Property == TextBox.SelectionStartProperty || e.Property == TextBox.SelectionEndProperty)
            QueueSuggestionsRefresh();
    }

    private void QueueSuggestionsRefresh()
    {
        if (_suggestionsRefreshQueued)
            return;

        _suggestionsRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _suggestionsRefreshQueued = false;
            RefreshSuggestions();
        }, DispatcherPriority.Input);
    }

    private void RefreshSuggestions()
    {
        if (!_attached || !IsEffectivelyVisible || !PromptBox.IsFocused || _viewModel is not { CanManage: true } model
            || HasActiveComposition() || !TryGetToken(out var token))
        {
            _dismissedToken = null;
            EndSuggestions();
            return;
        }
        if (_dismissedToken == token)
            return;

        model.DiscoverComposerSuggestions(token.Trigger);
        var suggestions = model.GetComposerSuggestions(token);
        if (suggestions.Length == 0 || ComposerCard.Bounds.Width <= 0)
        {
            CloseSuggestions();
            return;
        }

        var selected = _suggestionToken == token ? SuggestionsList.SelectedItem as AgentComposerSuggestionRow : null;
        if (SuggestionsList.ItemsSource is not IEnumerable<AgentComposerSuggestionRow> previous || !previous.SequenceEqual(suggestions))
        {
            SuggestionsList.ItemsSource = suggestions;
            SuggestionsList.SelectedIndex = Math.Max(0, Array.IndexOf(suggestions, selected));
        }
        else if (_suggestionToken != token)
        {
            SuggestionsList.SelectedIndex = 0;
        }
        _suggestionToken = token;
        SuggestionsCard.Width = ComposerCard.Bounds.Width;
        if (TopLevel.GetTopLevel(this) is { } top && ComposerCard.TranslatePoint(default, top) is { } anchor)
            SuggestionsList.MaxHeight = Math.Clamp(anchor.Y - 24, 70, 230);
        SuggestionsPopup.IsOpen = true;
    }

    private bool TryGetToken(out AgentComposerToken token)
    {
        token = default;
        return PromptBox.SelectionStart == PromptBox.SelectionEnd
            && AgentComposerToken.TryFind(PromptBox.Text ?? "", PromptBox.CaretIndex, out token);
    }

    private bool HandleSuggestionKey(Key key)
    {
        switch (key)
        {
            case Key.Up:
            case Key.Down:
                int delta = key == Key.Up ? -1 : 1;
                int index = Math.Clamp(SuggestionsList.SelectedIndex + delta, 0, SuggestionsList.ItemCount - 1);
                SuggestionsList.SelectedIndex = index;
                SuggestionsList.ScrollIntoView(SuggestionsList.SelectedItem!);
                return true;
            case Key.Tab:
            case Key.Enter:
                if (SuggestionsList.SelectedItem is AgentComposerSuggestionRow selection)
                    AcceptSuggestion(selection);
                return true;
            case Key.Escape:
                _dismissedToken = _suggestionToken;
                EndSuggestions();
                return true;
            default:
                return false;
        }
    }

    private void OnSuggestionPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && e.Source is Visual visual
            && visual.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is AgentComposerSuggestionRow selection)
        {
            e.Handled = true;
            AcceptSuggestion(selection);
        }
    }

    private void AcceptSuggestion(AgentComposerSuggestionRow suggestion)
    {
        if (HasActiveComposition() || !TryGetToken(out var token) || _suggestionToken != token
            || PromptBox.Text is not { } text || _viewModel is not { } model)
            return;

        string replacement = model.AcceptComposerSuggestion(suggestion);
        string suffix = text[token.End..];
        string separator = suffix.StartsWith(' ') ? "" : " ";
        PromptBox.SetCurrentValue(TextBox.TextProperty, text[..token.Start] + replacement + separator + suffix);
        int caret = token.Start + replacement.Length + 1;
        PromptBox.Focus();
        PromptBox.SelectionStart = caret;
        PromptBox.SelectionEnd = caret;
        PromptBox.CaretIndex = caret;
        EndSuggestions();
    }

    private void EndSuggestions()
    {
        CloseSuggestions();
        _viewModel?.ResetMentionCandidates();
    }

    private void CloseSuggestions()
    {
        SuggestionsPopup.IsOpen = false;
        _suggestionToken = null;
    }

    private void OnModelPickerOpening(object? sender, EventArgs e)
    {
        if (sender is Flyout flyout && ModelButton.TranslatePoint(default, ComposerCard) is { } anchor)
        {
            flyout.VerticalOffset = -anchor.Y - 6;
            flyout.HorizontalOffset = ComposerCard.Bounds.Width - anchor.X - ModelButton.Bounds.Width;
        }
        var top = TopLevel.GetTopLevel(this);
        double height = top is null ? 380 : ComposerCard.TranslatePoint(default, top)?.Y ?? top.Bounds.Height;
        ModelPicker.Resize(ComposerCard.Bounds.Width, height);
    }

    private async void OnModelPickerOpened(object? sender, EventArgs e)
    {
        if (DataContext is AgentTabViewModel viewModel)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ModelButton.Flyout?.IsOpen == true)
                    ModelPicker.FocusSearch();
            }, DispatcherPriority.Input);
            await viewModel.OpenModelPickerAsync();
        }
    }

    private async void OnAttachFile(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AgentTabViewModel viewModel || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;
        await viewModel.PickFilesAsync(async () =>
        {
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = AgentStrings.AttachFile,
                AllowMultiple = true
            });
            using var ownership = SharedFilePickerOptions.OwnStorageFiles(files);
            return files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
        });
    }
}
