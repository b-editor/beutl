using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Xaml.Interactivity;

using BeUtl.Animation.Easings;
using BeUtl.Controls.Behaviors;
using BeUtl.ViewModels.Tools;

namespace BeUtl.Views.Tools;

public partial class AnimationSpanEditor : UserControl
{
    public AnimationSpanEditor()
    {
        InitializeComponent();
        Interaction.SetBehaviors(this, new BehaviorCollection
        {
            new _DragBehavior
            {
                Orientation = Orientation.Vertical,
                DragControl = dragBorder
            }
        });

        backgroundBorder.AddHandler(DragDrop.DragOverEvent, BackgroundBorder_DragOver);
        backgroundBorder.AddHandler(DragDrop.DragLeaveEvent, BackgroundBorder_DragLeave);
        topBorder.AddHandler(DragDrop.DropEvent, TopBorder_Drop);
        bottomBorder.AddHandler(DragDrop.DropEvent, BottomBorder_Drop);
        backgroundBorder.AddHandler(DragDrop.DropEvent, BackgroundBorder_Drop);
    }

    private void BackgroundBorder_Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("Easing") is Easing easing &&
            DataContext is AnimationSpanEditorViewModel vm)
        {
            vm.SetEasing(vm.Model.Easing, easing);
            SetDropAreaClasses(false);
            e.Handled = true;
        }
    }

    private void TopBorder_Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("Easing") is Easing easing &&
            DataContext is AnimationSpanEditorViewModel vm)
        {
            vm.InsertForward(easing);
            SetDropAreaClasses(false);
            e.Handled = true;
        }
    }

    private void BottomBorder_Drop(object? sender, DragEventArgs e)
    {
        if (e.Data.Get("Easing") is Easing easing &&
            DataContext is AnimationSpanEditorViewModel vm)
        {
            vm.InsertBackward(easing);
            SetDropAreaClasses(false);
            e.Handled = true;
        }
    }

    private void BackgroundBorder_DragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains("Easing"))
        {
            SetDropAreaClasses(true);
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private void BackgroundBorder_DragLeave(object? sender, RoutedEventArgs e)
    {
        SetDropAreaClasses(false);
    }

    private void SetDropAreaClasses(bool value)
    {
        topBorder.Classes.Set("droparea", value);
        bottomBorder.Classes.Set("droparea", value);
    }

    public void Move(int newIndex, int oldIndex)
    {
        if (DataContext is AnimationSpanEditorViewModel viewModel)
        {
            viewModel.Move(newIndex, oldIndex);
        }
    }

    public void Remove_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AnimationSpanEditorViewModel viewModel)
        {
            viewModel.RemoveItem();
        }
    }

    private sealed class _DragBehavior : GenericDragBehavior
    {
        protected override void OnMoveDraggedItem(ItemsControl? itemsControl, int oldIndex, int newIndex)
        {
            if (AssociatedObject?.DataContext is AnimationSpanEditorViewModel viewModel)
            {
                viewModel.Move(newIndex, oldIndex);
            }
        }
    }
}
