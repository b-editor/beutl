using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;

using BeUtl.ViewModels;
using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public partial class NavigateButton : UserControl
{
    public NavigateButton()
    {
        InitializeComponent();
    }

    private void Navigate_Click(object? sender, RoutedEventArgs e)
    {
        if (this.FindLogicalAncestorOfType<EditView>().DataContext is EditViewModel editViewModel
            && DataContext is INavigationButtonViewModel viewModel)
        {
            ObjectPropertyEditorViewModel objViewModel
                = editViewModel.FindToolTab<ObjectPropertyEditorViewModel>()
                    ?? new ObjectPropertyEditorViewModel(editViewModel);

            objViewModel.NavigateCore(viewModel.GetObject(), false);
            editViewModel.OpenToolTab(objViewModel);
        }
    }

    private void Menu_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button)
        {
            button.ContextMenu?.Open();
        }
    }

    private void New_Click(object? sender, RoutedEventArgs e)
    {

    }

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {

    }
}
