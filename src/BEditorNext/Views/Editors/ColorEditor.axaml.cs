using Avalonia.Controls;

using BEditorNext.ViewModels.Editors;

using FluentAvalonia.UI.Controls;

namespace BEditorNext.Views.Editors;

public partial class ColorEditor : UserControl
{
    public ColorEditor()
    {
        InitializeComponent();
        colorPicker.ColorChanged += ColorPicker_ColorChanged;
    }

    private void ColorPicker_ColorChanged(ColorPickerButton sender, ColorChangedEventArgs e)
    {
        if (DataContext is ColorEditorViewModel vm)
        {
            vm.SetValue(
                vm.Setter.Value,
                ((Avalonia.Media.Color)e.NewColor).ToMedia());
        }
    }
}
