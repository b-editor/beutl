using Avalonia.Controls;
using Avalonia.Interactivity;

using BeUtl.ViewModels.AnimationEditors;

namespace BeUtl.Views.AnimationEditors;

public sealed partial class BooleanAnimationEditor : UserControl
{
    public BooleanAnimationEditor()
    {
        InitializeComponent();
        prevCheckBox.Click += PrevCheckBox_Click;
        nextCheckBox.Click += NextCheckBox_Click;
    }

    private void PrevCheckBox_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AnimationEditorViewModel<bool> vm)
        {
            vm.SetPrevious(vm.Animation.Previous, (bool)(prevCheckBox.IsChecked!));
        }
    }

    private void NextCheckBox_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AnimationEditorViewModel<bool> vm)
        {
            vm.SetNext(vm.Animation.Next, (bool)(nextCheckBox.IsChecked!));
        }
    }
}
