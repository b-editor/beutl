using Avalonia.Controls;
using Avalonia.Interactivity;

using BEditorNext.ViewModels.AnimationEditors;

namespace BEditorNext.Views.AnimationEditors;

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
            vm.SetPrevious(vm.Animation.Previous, prevCheckBox.IsChecked ?? vm.Setter.Property.GetDefaultValue());
        }
    }

    private void NextCheckBox_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AnimationEditorViewModel<bool> vm)
        {
            vm.SetNext(vm.Animation.Next, nextCheckBox.IsChecked ?? vm.Setter.Property.GetDefaultValue());
        }
    }
}
