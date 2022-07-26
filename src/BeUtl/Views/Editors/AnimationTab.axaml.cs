using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

using BeUtl.Animation.Easings;
using BeUtl.ViewModels.Editors;

namespace BeUtl.Views.Editors;
public partial class AnimationTab : UserControl
{
    public AnimationTab()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    private void Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("Easing") is Easing easing
            && DataContext is AnimationTabViewModel viewModel)
        {
            viewModel.AddAnimation(easing);
            e.Handled = true;
        }
    }

    private void DragOver(object? sender, DragEventArgs e)
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
}
