using Avalonia.Controls;
using Avalonia.Interactivity;
using Beutl.Editor.Components.VersionControlTab.ViewModels;

namespace Beutl.Editor.Components.VersionControlTab.Views;

internal sealed partial class VersionControlChangesView : UserControl
{
    public VersionControlChangesView()
    {
        InitializeComponent();
    }

    private async void OnChangedFileSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (IsEffectivelyVisible
            && DataContext is VersionControlTabViewModel viewModel
            && sender is ListBox listBox)
        {
            await viewModel.SelectFileAsync(
                listBox.SelectedItem as VersionControlFileChangeViewModel);
        }
    }
}
