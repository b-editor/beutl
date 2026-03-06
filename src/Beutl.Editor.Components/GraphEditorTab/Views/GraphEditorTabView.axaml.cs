using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public partial class GraphEditorTabView : UserControl
{
    public GraphEditorTabView()
    {
        InitializeComponent();
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (DataContext is GraphEditorTabViewModel viewModel)
        {
            viewModel.Refresh();
        }
    }

    private void ToggleDragModeClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;
        if (DataContext is not GraphEditorTabViewModel { SelectedAnimation.Value: { } viewModel }) return;

        viewModel.Symmetry.Value = false;
        viewModel.Asymmetry.Value = false;
        viewModel.Separately.Value = false;

        switch (tag)
        {
            case "Symmetry":
                viewModel.Symmetry.Value = true;
                break;
            case "Asymmetry":
                viewModel.Asymmetry.Value = true;
                break;
            case "Separately":
                viewModel.Separately.Value = true;
                break;
        }
    }
}
