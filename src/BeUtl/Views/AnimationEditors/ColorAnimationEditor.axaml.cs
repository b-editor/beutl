using Avalonia.Controls;

using BeUtl.ViewModels.AnimationEditors;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Views.AnimationEditors;

public sealed partial class ColorAnimationEditor : UserControl
{
    public ColorAnimationEditor()
    {
        InitializeComponent();
        prevColorPicker.ColorChanged += PrevColorPicker_ColorChanged;
        nextColorPicker.ColorChanged += NextColorPicker_ColorChanged;
    }

    private void PrevColorPicker_ColorChanged(ColorPickerButton sender, ColorChangedEventArgs e)
    {
        if (DataContext is ColorAnimationEditorViewModel vm)
        {
            vm.SetPrevious(vm.Animation.Previous, ((Avalonia.Media.Color)e.NewColor).ToMedia());
        }
    }

    private void NextColorPicker_ColorChanged(ColorPickerButton sender, ColorChangedEventArgs e)
    {
        if (DataContext is ColorAnimationEditorViewModel vm)
        {
            vm.SetNext(vm.Animation.Next, ((Avalonia.Media.Color)e.NewColor).ToMedia());
        }
    }
}
