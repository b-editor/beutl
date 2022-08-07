using Avalonia.Controls;
using Avalonia.Interactivity;

using BeUtl.Media;
using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;

public partial class AlignmentsEditor : UserControl
{
    public AlignmentsEditor()
    {
        InitializeComponent();
    }

    private void Left_Checked(object? sender, RoutedEventArgs e)
    {
        SetValue(AlignmentX.Left);
    }

    private void HorizontalCenter_Checked(object? sender, RoutedEventArgs e)
    {
        SetValue(AlignmentX.Center);
    }

    private void Right_Checked(object? sender, RoutedEventArgs e)
    {
        SetValue(AlignmentX.Right);
    }

    private void SetValue(AlignmentX value)
    {
        if (DataContext is AlignmentsEditorViewModel viewModel)
        {
            viewModel.SetAlignmentX(value);
        }
    }

    private void Top_Checked(object? sender, RoutedEventArgs e)
    {
        SetValue(AlignmentY.Top);
    }

    private void VerticalCenter_Checked(object? sender, RoutedEventArgs e)
    {
        SetValue(AlignmentY.Center);
    }

    private void Bottom_Checked(object? sender, RoutedEventArgs e)
    {
        SetValue(AlignmentY.Bottom);
    }

    private void SetValue(AlignmentY value)
    {
        if (DataContext is AlignmentsEditorViewModel viewModel)
        {
            viewModel.SetAlignmentY(value);
        }
    }
}
