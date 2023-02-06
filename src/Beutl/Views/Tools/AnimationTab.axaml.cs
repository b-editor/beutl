using Avalonia.Controls;
using Avalonia.Input;

using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.ViewModels.Tools;

namespace Beutl.Views.Tools;
public partial class AnimationTab : UserControl
{
    public AnimationTab()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, DragOver);
        AddHandler(DragDrop.DropEvent, Drop);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is AnimationTabViewModel viewModel)
        {
            var self = new WeakReference<AnimationTab>(this);
            viewModel.RequestScroll = obj =>
            {
                if (self.TryGetTarget(out AnimationTab? @this) && @this.DataContext is AnimationTabViewModel viewModel)
                {
                    int index = 0;
                    bool found = false;
                    for (; index < viewModel.Items.Count; index++)
                    {
                        AnimationSpanEditorViewModel? item = viewModel.Items[index];
                        if (ReferenceEquals(item?.Model, obj))
                        {
                            found = true;
                            break;
                        }
                    }

                    if (found)
                    {
                        Control? ctrl = @this.itemsControl.ContainerFromIndex(index);
                        if (ctrl != null)
                        {
                            @this.scrollViewer.Offset = new Avalonia.Vector(@this.scrollViewer.Offset.X, ctrl.Bounds.Top);
                        }
                    }
                }
            };
        }
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
