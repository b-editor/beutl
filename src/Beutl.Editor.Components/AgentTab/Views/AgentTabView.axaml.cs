using Avalonia.Controls;
using Avalonia;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Editor.Components.AgentTab.ViewModels;
using Beutl.Editor.Components.Helpers;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentTabView : UserControl
{
    private const double WideLayoutThreshold = 550;
    private bool _escapePressed;
    private AgentTabViewModel? _focusModel;

    public AgentTabView()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnShortcutKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnShortcutKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        LostFocus += (_, _) => _escapePressed = false;
        Composer.PropertyChanged += (_, change) =>
        {
            if (change.Property == IsVisibleProperty && Composer.IsVisible)
                QueueComposerFocus();
        };
    }

    internal void FocusComposer() => Composer.FocusPrompt();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty)
            Classes.Set("wide", Bounds.Width >= WideLayoutThreshold);
    }

    protected override async void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        WatchComposerFocus(DataContext as AgentTabViewModel);
        if (DataContext is AgentTabViewModel viewModel)
            await viewModel.InitializeAsync();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        WatchComposerFocus(null);
        base.OnUnloaded(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (IsLoaded)
            WatchComposerFocus(DataContext as AgentTabViewModel);
    }

    private void WatchComposerFocus(AgentTabViewModel? model)
    {
        if (_focusModel is not null)
            _focusModel.FocusRequested -= QueueComposerFocus;
        _focusModel = model;
        if (_focusModel is not null)
        {
            _focusModel.FocusRequested += QueueComposerFocus;
            QueueComposerFocus();
        }
    }

    private void QueueComposerFocus()
    {
        if (_focusModel is { HasPendingComposerFocus: true } model)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ReferenceEquals(_focusModel, model) && model.HasPendingComposerFocus
                    && Composer.IsEffectivelyVisible && Composer.FocusPrompt())
                    model.HasPendingComposerFocus = false;
            }, DispatcherPriority.Background);
        }
    }

    internal static bool IsComposing(KeyEventArgs e)
        => e.Key == Key.ImeProcessed || e.Source is Visual visual
            && visual.FindAncestorOfType<TextBox>(includeSelf: true) is { } box
            && box.GetVisualDescendants().OfType<TextPresenter>().Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));

    private async void OnShortcutKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Escape or Key.Tab or Key.N or Key.K or Key.M)
            || DataContext is not AgentTabViewModel viewModel || IsComposing(e))
            return;

        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            if (FindOpenPopup(e) is { } dismiss)
            {
                e.Handled = true;
                viewModel.ClearEscapeStopHint();
                dismiss();
                _escapePressed = true;
            }
            else if (viewModel.CanStop)
            {
                e.Handled = true;
                if (_escapePressed)
                    return;
                _escapePressed = true;
                await viewModel.ConfirmStopShortcutAsync();
            }
            return;
        }

        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.Shift && viewModel.CanConfigure && FindOpenPopup(e) is null)
        {
            e.Handled = true;
            await viewModel.CyclePermissionModeAsync();
            return;
        }
        if (e.KeyModifiers != KeyGestureHelper.GetCommandModifier())
            return;

        switch (e.Key)
        {
            case Key.N when viewModel.CanManage:
                e.Handled = true;
                FindOpenPopup(e)?.Invoke();
                await viewModel.NewThreadAsync();
                FocusComposer();
                break;
            case Key.K when viewModel.CanManage:
                e.Handled = true;
                if (!ThreadBar.IsThreadPickerOpen)
                    FindOpenPopup(e)?.Invoke();
                ThreadBar.OpenThreadPicker();
                break;
            case Key.M when viewModel.CanChooseModel && viewModel.ShowComposer:
                e.Handled = true;
                if (!Composer.IsModelPickerOpen)
                    FindOpenPopup(e)?.Invoke();
                Composer.OpenModelPicker();
                break;
        }
    }

    private void OnShortcutKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            _escapePressed = false;
    }

    private Action? FindOpenPopup(KeyEventArgs e)
    {
        if (Composer.HasOpenSuggestions)
            return Composer.DismissSuggestions;
        if (e.Source is ILogical logical && logical.FindLogicalAncestorOfType<Popup>() is { IsOpen: true } popup)
            return popup.Close;

        foreach (var control in this.GetVisualDescendants().OfType<Control>())
        {
            if (control is ComboBox { IsDropDownOpen: true } combo)
                return () => combo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
            if (control.ContextMenu is { IsOpen: true } menu)
                return menu.Close;
            if (control.ContextFlyout is { IsOpen: true } context)
                return context.Hide;
            if (control is Button { Flyout: { IsOpen: true } } button)
                return button.Flyout!.Hide;
            if (FlyoutBase.GetAttachedFlyout(control) is { IsOpen: true } attached)
                return attached.Hide;
        }
        return null;
    }

    private void OnStarterPrompt(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AgentStarterPrompt starter } && DataContext is AgentTabViewModel { CanManage: true } viewModel)
        {
            viewModel.ApplyStarterPrompt(starter);
            Composer.FocusPrompt();
        }
    }

    private async void OnRecentThread(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AgentThreadRow row } && DataContext is AgentTabViewModel viewModel)
            await viewModel.SelectThreadAsync(row.Thread);
    }
}
