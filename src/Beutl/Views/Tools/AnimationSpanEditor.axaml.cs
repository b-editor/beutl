using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Xaml.Interactivity;

using Beutl.Animation.Easings;
using Beutl.Controls.Behaviors;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;

public partial class AnimationSpanEditor : UserControl
{
    public AnimationSpanEditor()
    {
        InitializeComponent();
        backgroundBorder.AddHandler(DragDrop.DragOverEvent, BackgroundBorder_DragOver);
        backgroundBorder.AddHandler(DragDrop.DropEvent, BackgroundBorder_Drop);
    }

    private void BackgroundBorder_Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("Easing") is Easing easing &&
            DataContext is AnimationSpanEditorViewModel vm)
        {
            vm.SetEasing(vm.Model.Easing, easing);
            e.Handled = true;
        }
    }

    private void BackgroundBorder_DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("Easing"))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    public void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AnimationSpanEditorViewModel viewModel)
        {
            viewModel.RemoveItem();
        }
    }
}
