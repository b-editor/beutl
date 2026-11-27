using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
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

    internal bool IsModelPickerOpen => ModelButton.Flyout?.IsOpen == true;

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
