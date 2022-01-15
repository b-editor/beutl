using Avalonia.Controls;

using BeUtl.ViewModels.Editors;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Views.Editors;

public sealed partial class ColorEditor : UserControl
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
            vm.SetValue(vm.Setter.Value, ((Avalonia.Media.Color)e.NewColor).ToMedia());
        }
    }
}
