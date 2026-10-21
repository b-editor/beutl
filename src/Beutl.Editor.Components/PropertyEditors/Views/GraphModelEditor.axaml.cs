using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;

using Beutl.Editor.Components.PropertyEditors.ViewModels;

namespace Beutl.Editor.Components.PropertyEditors.Views;

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
