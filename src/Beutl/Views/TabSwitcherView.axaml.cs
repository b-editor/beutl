using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Logging;
using Beutl.Services;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;
using Beutl.ViewModels.Dock;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;

namespace Beutl.Views;

public sealed partial class TabSwitcherView : UserControl
{
    private readonly CompositeDisposable _subscriptions = [];
    private readonly ILogger _logger = Log.CreateLogger<TabSwitcherView>();
    private WeakReference<IInputElement>? _previousFocus;
    private Window? _window;
    private bool _committing;

    public TabSwitcherView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateSwitcherLayout();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        BindViewModel();
    }

    private void BindViewModel()
    {
        _subscriptions.Clear();
        if (DataContext is not TabSwitcherViewModel vm) return;
        _subscriptions.Add(vm.IsOpen.Skip(1).Subscribe(open =>
        {
            if (open)
            {
                IInputElement? focused = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
                _previousFocus = focused is not null ? new(focused) : null;
                UpdateSwitcherLayout();
                FocusSelection();
            }
            else if (!_committing)
            {
                RestoreFocus();
            }
        }));
        _subscriptions.Add(vm.SelectedGroup.Subscribe(_ =>
        {
            UpdateSwitcherLayout();
            if (vm.IsOpen.Value) FocusSelection();
        }));
        _subscriptions.Add(vm.IsCreating.Subscribe(_ => UpdateSwitcherLayout()));
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        BindViewModel();
        _window = TopLevel.GetTopLevel(this) as Window;
        if (_window is not null) _window.Deactivated += OnWindowDeactivated;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_window is not null) _window.Deactivated -= OnWindowDeactivated;
        _window = null;
        (DataContext as TabSwitcherViewModel)?.Close();
        _previousFocus = null;
        _subscriptions.Clear();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnWindowDeactivated(object? sender, EventArgs e)
    {
        _previousFocus = null;
        (DataContext as TabSwitcherViewModel)?.Close();
    }

    internal void HandleKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || DataContext is not TabSwitcherViewModel vm || e.Key == Key.ImeProcessed) return;
        if (vm.IsOpen.Value)
        {
            switch (e.Key)
            {
                case Key.Tab:
                    if (vm.IsCreating.Value && FindNavigationCommand(e) is { } navigationName)
                        vm.ExecuteCommand(new(navigationName) { KeyEventArgs = e });
                    else
                        vm.MoveSelection(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                    break;
                case Key.F7 when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                    if (vm.IsCreating.Value)
                        vm.ExecuteCommand(new(e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                            ? MainViewExtension.PreviousToolTabCommandName : MainViewExtension.NextToolTabCommandName)
                        { KeyEventArgs = e });
                    else
                        vm.MoveSelection(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? -1 : 1);
                    break;
                case Key.Down: vm.MoveSelection(1); break;
                case Key.Up: vm.MoveSelection(-1); break;
                case Key.Left: vm.MoveGroup(-1); break;
                case Key.Right: vm.MoveGroup(1); break;
                case Key.Home: vm.Select(vm.SelectedGroup.Value, 0); break;
                case Key.End: vm.Select(vm.SelectedGroup.Value, vm.Items(vm.SelectedGroup.Value).Count - 1); break;
                case Key.Enter: Commit(); break;
                case Key.Escape: vm.Close(); break;
                default:
                    if (FindNavigationCommand(e) is { } name)
                    {
                        vm.ExecuteCommand(new(name) { KeyEventArgs = e });
                        FocusSelection();
                    }
                    // Keep editor and shell commands from running behind the switcher.
                    e.Handled = true;
                    return;
            }
            e.Handled = true;
            ScrollSelectionIntoView();
            if (vm.IsOpen.Value) FocusSelection();
            return;
        }

        if (this.FindAncestorOfType<MainView>() is not { IsEnabled: true, DataContext: MainViewModel main }
            || FindNavigationCommand(e) is not { } commandName)
            return;

        e.Handled = main.CommandPalette.IsOpen.Value
                    || (e.Source as Visual)?.FindAncestorOfType<FAContentDialog>(includeSelf: true) is not null
                    || vm.ExecuteCommand(new(commandName) { KeyEventArgs = e });
    }

    private string? FindNavigationCommand(KeyEventArgs e)
    {
        if (this.FindAncestorOfType<MainView>()?.DataContext is not MainViewModel main) return null;
        OSPlatform platform = OperatingSystem.IsWindows() ? OSPlatform.Windows
            : OperatingSystem.IsMacOS() ? OSPlatform.OSX : OSPlatform.Linux;
        return main.ContextCommandManager?.GetDefinitions<MainViewExtension>()
            .FirstOrDefault(command => TabSwitcherViewModel.IsNavigationCommand(command.Definition.Name)
                                       && command.KeyGestures.Any(gesture => gesture.Platform == platform
                                           && gesture.KeyGesture?.Matches(e) == true))?.Definition.Name;
    }

    internal void HandleKeyUp(object? sender, KeyEventArgs e)
    {
        if (DataContext is not TabSwitcherViewModel { IsOpen.Value: true } vm
            || vm.HeldModifiers == KeyModifiers.None)
            return;
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin
            && (e.KeyModifiers & vm.HeldModifiers) == KeyModifiers.None)
        {
            e.Handled = true;
            Commit();
        }
    }

    private void Commit()
    {
        if (DataContext is not TabSwitcherViewModel vm) return;
        _committing = true;
        try
        {
            TabSwitcherItem? activated = vm.Commit();
            if (activated is null)
                RestoreFocus();
            else
                FocusActivatedTab(activated);
        }
        catch (Exception ex)
        {
            vm.Close();
            RestoreFocus();
            _logger.LogError(ex, "Failed to switch or create a tab.");
            NotificationService.ShowError(Strings.TabSwitcher_Title, MessageStrings.OperationFailed);
        }
        finally
        {
            _committing = false;
        }
    }

    private void FocusSelection()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is not TabSwitcherViewModel { IsOpen.Value: true } vm) return;
            ListBox list = List(vm.SelectedGroup.Value);
            if (list.SelectedItem is { } item)
            {
                list.ScrollIntoView(item);
                if (list.ContainerFromItem(item) is ListBoxItem container)
                    container.Focus();
                else
                    list.Focus();
            }
        }, DispatcherPriority.Background);
    }

    private void RestoreFocus()
    {
        WeakReference<IInputElement>? previous = _previousFocus;
        _previousFocus = null;
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is TabSwitcherViewModel { IsOpen.Value: true }) return;
            if (previous?.TryGetTarget(out IInputElement? element) == true
                && element is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } control
                && TopLevel.GetTopLevel(control) is not null)
                element.Focus();
        }, DispatcherPriority.Background);
    }

    private void FocusActivatedTab(TabSwitcherItem item)
    {
        _previousFocus = null;
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is TabSwitcherViewModel { IsOpen.Value: true }) return;
            var main = this.FindAncestorOfType<MainView>();
            Visual? root = item.Tool is BeutlToolDockable { ToolContent: { } toolContent }
                ? TopLevel.GetTopLevel(toolContent) : main;
            Control? content = item.Tool is { } tool
                ? root?.GetVisualDescendants().OfType<ToolTabContent>()
                    .FirstOrDefault(control => ReferenceEquals(control.DataContext, tool))
                : main?.FindControl<EditorHostView>("EditorHost");
            Control? focusTarget = content?.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(control => control.Focusable && control.IsEffectivelyVisible && control.IsEffectivelyEnabled);
            (focusTarget ?? content ?? main)?.Focus();
        }, DispatcherPriority.Background);
    }

    private ListBox List(TabSwitcherGroup group) => group switch
    {
        TabSwitcherGroup.Documents => DocumentsList,
        TabSwitcherGroup.Tools => ToolsList,
        _ => NewToolsList,
    };

    private void ScrollSelectionIntoView()
    {
        if (DataContext is TabSwitcherViewModel { IsOpen.Value: true } vm
            && vm.SelectedItem is { } item)
            List(vm.SelectedGroup.Value).ScrollIntoView(item);
    }

    private void UpdateSwitcherLayout()
    {
        bool creating = DataContext is TabSwitcherViewModel { IsCreating.Value: true };
        SwitcherContainer.Width = Math.Min(creating ? 360 : 640, Math.Max(0, Bounds.Width - 32));
        SwitcherContainer.MaxHeight = Math.Min(creating ? 390 : 340, Math.Max(0, Bounds.Height - 32));
    }

    private void OnCreateTabClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TabSwitcherViewModel vm)
        {
            vm.Begin(TabSwitcherGroup.NewTools, 1, KeyModifiers.None);
            FocusSelection();
        }
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is TabSwitcherViewModel vm)
            vm.Begin(TabSwitcherGroup.Documents, 1, KeyModifiers.None);
    }

    private void OnItemTapped(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not null)
        {
            Commit();
            e.Handled = true;
        }
    }

    private void OnBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        (DataContext as TabSwitcherViewModel)?.Close();
        e.Handled = true;
    }
}
