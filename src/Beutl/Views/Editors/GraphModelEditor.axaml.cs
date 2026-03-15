using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class GraphModelEditor : UserControl
{
    public GraphModelEditor()
    {
        InitializeComponent();
    }

    private void OpenNodeGraphTab_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is GraphModelEditorViewModel { IsDisposed: false } viewModel)
        {
            viewModel.OpenNodeGraphTab();
        }
    }
}
