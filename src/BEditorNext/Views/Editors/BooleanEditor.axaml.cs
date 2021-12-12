using Avalonia.Controls;
using Avalonia.Interactivity;

using BEditorNext.ViewModels.Editors;

namespace BEditorNext.Views.Editors;

public partial class BooleanEditor : UserControl
{
    public BooleanEditor()
    {
        InitializeComponent();
    }

    private void CheckBox_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BooleanEditorViewModel vm ||
            sender is not CheckBox checkBox)
        {
            return;
        }

        vm.SetValue(
            vm.Value.Value,
            checkBox.IsChecked ?? vm.Setter.Property.GetDefaultValue());
    }
}
