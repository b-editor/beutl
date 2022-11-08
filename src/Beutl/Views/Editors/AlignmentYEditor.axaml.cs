using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

using Beutl.Media;
using Beutl.ViewModels.Editors;

namespace Beutl.Views.Editors;

public partial class AlignmentYEditor : UserControl
{
    public AlignmentYEditor()
    {
        InitializeComponent();
    }

    private void Top_Checked(object? sender, RoutedEventArgs e)
    {
        ChangeValue(AlignmentY.Top);
    }

    private void Center_Checked(object? sender, RoutedEventArgs e)
    {
        ChangeValue(AlignmentY.Center);
    }

    private void Bottom_Checked(object? sender, RoutedEventArgs e)
    {
        ChangeValue(AlignmentY.Bottom);
    }

    private void ChangeValue(AlignmentY value)
    {
        if (DataContext is AlignmentYEditorViewModel viewModel)
        {
            viewModel.SetValue(viewModel.Value.Value, value);
        }
    }
}
