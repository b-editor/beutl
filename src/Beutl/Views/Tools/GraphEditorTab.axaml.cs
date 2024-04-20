using Avalonia.Controls;
using Avalonia.Input;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;
public partial class GraphEditorTab : UserControl
{
    public GraphEditorTab()
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
}
