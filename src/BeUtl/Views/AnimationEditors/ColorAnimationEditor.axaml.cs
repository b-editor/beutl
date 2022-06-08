using Avalonia.Controls;

using BeUtl.ViewModels.AnimationEditors;

using FluentAvalonia.UI.Controls;

namespace BeUtl.Views.AnimationEditors;

public sealed partial class ColorAnimationEditor : UserControl
{
    public ColorAnimationEditor()
    {
        InitializeComponent();
        prevColorPicker.FlyoutConfirmed += PrevColorPicker_ColorChanged;
        nextColorPicker.FlyoutConfirmed += NextColorPicker_ColorChanged;
    }

    private void PrevColorPicker_ColorChanged(ColorPickerButton sender, ColorButtonColorChangedEventArgs e)
    {
        Avalonia.Media.Color? newColor = e.NewColor;
        if (DataContext is ColorAnimationEditorViewModel vm && newColor.HasValue)
        {
            vm.SetPrevious(vm.Animation.Next, newColor.Value.ToMedia());
        }
    }

    private void NextColorPicker_ColorChanged(ColorPickerButton sender, ColorButtonColorChangedEventArgs e)
    {
        Avalonia.Media.Color? newColor = e.NewColor;
        if (DataContext is ColorAnimationEditorViewModel vm && newColor.HasValue)
        {
            vm.SetNext(vm.Animation.Next, newColor.Value.ToMedia());
        }
    }
}
