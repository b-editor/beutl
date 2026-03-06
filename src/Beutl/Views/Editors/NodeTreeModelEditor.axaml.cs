using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class NodeTreeModelEditor : UserControl
{
    public NodeTreeModelEditor()
    {
        InitializeComponent();
    }

    private void OpenNodeTreeTab_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is NodeTreeModelEditorViewModel { IsDisposed: false } viewModel)
        {
            viewModel.OpenNodeTreeTab();
        }
    }
}
