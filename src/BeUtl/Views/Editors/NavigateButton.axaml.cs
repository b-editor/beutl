using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

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
        if (this.FindLogicalAncestorOfType<ObjectPropertyEditor>().DataContext is ObjectPropertyEditorViewModel parentViewModel
            && DataContext is ObjectPropertyEditorViewModel.INavigationButtonViewModel viewModel)
        {
            parentViewModel.NavigateCore(viewModel.GetObject(), false);
        }
    }
}
