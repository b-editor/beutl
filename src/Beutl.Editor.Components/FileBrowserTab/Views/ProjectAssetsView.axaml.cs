using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;

namespace Beutl.Editor.Components.FileBrowserTab.Views;

public sealed partial class ProjectAssetsView : UserControl
{
    public ProjectAssetsView() => InitializeComponent();

    private ProjectAssetsViewModel? ViewModel => DataContext as ProjectAssetsViewModel;

    private async void OnApply(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is { } vm) await vm.ApplyAsync(delete: vm.DeleteConfirmation.Value != null);
    }

    private void OnSecondary(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        if (vm.DeleteConfirmation.Value != null) vm.CancelDelete();
        else vm.BeginDelete();
    }

    private void OnToggleSelection(object? sender, RoutedEventArgs e) => ViewModel?.ToggleSelection();
    private void OnCancel(object? sender, RoutedEventArgs e) => ViewModel?.Cancel();

    private void OnAssetRowTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { DataContext: ProjectAssetRow { CanSelect: true } row }
            || e.Source is not Visual source || source.FindAncestorOfType<CheckBox>(includeSelf: true) != null)
            return;

        row.IsSelected = !row.IsSelected;
        e.Handled = true;
    }
}
