using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Editor.Components.AgentTab.ViewModels;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed partial class AgentPermissionCard : UserControl
{
    public AgentPermissionCard()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnApprovalKeyDown, RoutingStrategies.Tunnel);
        PermissionOptions.AddHandler(DoubleTappedEvent, OnOptionDoubleTapped, handledEventsToo: true);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        FocusOptions();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (IsLoaded && (change.Property == DataContextProperty || change.Property == IsVisibleProperty))
            Dispatcher.UIThread.Post(FocusOptions, DispatcherPriority.Input);
    }

    private void FocusOptions()
    {
        if (IsLoaded && IsEffectivelyVisible && DataContext is AgentPermissionItem { IsPending: true })
            PermissionOptions.Focus();
    }

    private void OnApprovalKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers != KeyModifiers.None || DataContext is not AgentPermissionItem { IsPending: true } item)
            return;

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            item.SubmitSelected();
            return;
        }
        int index = e.Key switch
        {
            Key.Up => Math.Max(0, item.SelectedIndex - 1),
            Key.Down => Math.Min(item.Options.Count - 1, item.SelectedIndex + 1),
            >= Key.D1 and <= Key.D3 => e.Key - Key.D1,
            >= Key.NumPad1 and <= Key.NumPad3 => e.Key - Key.NumPad1,
            _ => -1
        };
        if (index >= 0 && index < item.Options.Count)
        {
            e.Handled = true;
            item.SelectedIndex = index;
        }
    }

    private void OnOptionDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is AgentPermissionItem { IsPending: true } item
            && e.Source is Visual visual
            && visual.FindAncestorOfType<ListBoxItem>(includeSelf: true)?.DataContext is AgentPermissionChoice choice)
        {
            item.SelectedIndex = choice.Number - 1;
            item.SubmitSelected();
            e.Handled = true;
        }
    }
}
