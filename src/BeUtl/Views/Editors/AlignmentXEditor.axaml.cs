using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using BeUtl.Media;
using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public partial class AlignmentXEditor : UserControl
{
    public AlignmentXEditor()
    {
        InitializeComponent();
    }

    private void Left_Checked(object? sender, RoutedEventArgs e)
    {
        ChangeValue(AlignmentX.Left);
    }

    private void Center_Checked(object? sender, RoutedEventArgs e)
    {
        ChangeValue(AlignmentX.Center);
    }

    private void Right_Checked(object? sender, RoutedEventArgs e)
    {
        ChangeValue(AlignmentX.Right);
    }

    private void ChangeValue(AlignmentX value)
    {
        if (DataContext is AlignmentXEditorViewModel viewModel)
        {
            viewModel.SetValue(viewModel.Value.Value, value);
        }
    }
}
