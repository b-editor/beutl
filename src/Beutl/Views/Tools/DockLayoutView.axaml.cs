using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Beutl.Controls;
using Beutl.Services;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;

public partial class DockLayoutView : UserControl
{
    public DockLayoutView()
    {
        InitializeComponent();
    }

    private DockLayoutViewModel? ViewModel => DataContext as DockLayoutViewModel;

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not Control anchor) return;

        var flyout = new RenameFlyout { Text = viewModel.SuggestName() };
        flyout.Confirmed += (_, name) => viewModel.Save(name);
        flyout.ShowAt(anchor);
    }

    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;

        // DoubleTapped is attached to the ListBox, so a double click on a row action bubbles here
        // too. Applying on top of the action the user actually pressed would be a surprise.
        if (e.Source is ILogical source
            && source.GetSelfAndLogicalAncestors().OfType<Button>().Any())
        {
            return;
        }

        if (ItemFrom(e.Source) is not { } item) return;

        viewModel.Apply(item);
        e.Handled = true;
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && ItemFrom(sender) is { } item)
        {
            viewModel.Apply(item);
        }
    }

    private void OnRenameClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;
        if (sender is not Control anchor || ItemFrom(sender) is not { } item) return;

        var flyout = new RenameFlyout { Text = item.Name.Value };
        flyout.Confirmed += (_, name) => viewModel.Rename(item, name);
        flyout.ShowAt(anchor);
    }

    private void OnRemoveClick(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } viewModel && ItemFrom(sender) is { } item)
        {
            viewModel.Remove(item);
        }
    }

    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ResetLayout();
    }

    // The acted-on layout comes from the row's DataContext, not the list selection.
    private static DockLayoutPresetItem? ItemFrom(object? source)
    {
        return (source as Control)?.DataContext as DockLayoutPresetItem
               ?? (source as ILogical)?.GetLogicalAncestors()
               .OfType<Control>()
               .Select(c => c.DataContext)
               .OfType<DockLayoutPresetItem>()
               .FirstOrDefault();
    }
}
