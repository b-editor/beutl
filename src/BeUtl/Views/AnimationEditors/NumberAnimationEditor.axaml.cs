using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using BeUtl.Services.Editors;
using BeUtl.ViewModels;
using BeUtl.ViewModels.AnimationEditors;

namespace BeUtl.Views.AnimationEditors;

public partial class NumberAnimationEditor : UserControl
{
    public NumberAnimationEditor()
    {
        InitializeComponent();
    }
}

public class NumberAnimationEditor<T> : NumberAnimationEditor
    where T : struct
{
    private T _oldPrev;
    private T _oldNext;

    public NumberAnimationEditor()
    {
        prevTextBox.GotFocus += PreviousTextBox_GotFocus;
        nextTextBox.GotFocus += NextTextBox_GotFocus;
        prevTextBox.LostFocus += PreviousTextBox_LostFocus;
        nextTextBox.LostFocus += NextTextBox_LostFocus;
        prevTextBox.AddHandler(PointerWheelChangedEvent, PreviousTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
        nextTextBox.AddHandler(PointerWheelChangedEvent, NextTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);

        prevTextBox.GetObservable(TextBox.TextProperty).Subscribe(PreviousTextBox_TextChanged);
        nextTextBox.GetObservable(TextBox.TextProperty).Subscribe(NextTextBox_TextChanged);
    }

    private void PreviousTextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is not AnimationEditorViewModel<T> vm) return;

        _oldPrev = vm.Animation.Previous;
    }

    private void NextTextBox_GotFocus(object? sender, GotFocusEventArgs e)
    {
        if (DataContext is not AnimationEditorViewModel<T> vm) return;

        _oldNext = vm.Animation.Next;
    }

    private void PreviousTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AnimationEditorViewModel<T> vm ||
            vm.Description.NumberEditorService is not INumberEditorService<T> numService)
        {
            return;
        }

        if (numService.TryParse(prevTextBox.Text, out T newValue))
        {
            vm.SetPrevious(_oldPrev, newValue);
        }
    }

    private void NextTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AnimationEditorViewModel<T> vm ||
            vm.Description.NumberEditorService is not INumberEditorService<T> numService)
        {
            return;
        }

        if (numService.TryParse(nextTextBox.Text, out T newValue))
        {
            vm.SetNext(_oldNext, newValue);
        }
    }

    private void PreviousTextBox_TextChanged(string s)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (DataContext is not AnimationEditorViewModel<T> vm ||
                vm.Description.NumberEditorService is not INumberEditorService<T> numService)
            {
                return;
            }

            await Task.Delay(10);

            if (numService.TryParse(prevTextBox.Text, out T value))
            {
                vm.Animation.Previous = numService.Clamp(value, numService.GetMinimum(vm.Setter), numService.GetMaximum(vm.Setter));
            }
        });
    }

    private void NextTextBox_TextChanged(string s)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (DataContext is not AnimationEditorViewModel<T> vm ||
                vm.Description.NumberEditorService is not INumberEditorService<T> numService)
            {
                return;
            }

            await Task.Delay(10);

            if (numService.TryParse(nextTextBox.Text, out T value))
            {
                vm.Animation.Next = numService.Clamp(value, numService.GetMinimum(vm.Setter), numService.GetMaximum(vm.Setter));
            }
        });
    }

    private void PreviousTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not AnimationEditorViewModel<T> vm ||
            vm.Description.NumberEditorService is not INumberEditorService<T> numService)
        {
            return;
        }

        if (prevTextBox.IsKeyboardFocusWithin && numService.TryParse(prevTextBox.Text, out T value))
        {
            value = e.Delta.Y switch
            {
                < 0 => numService.Decrement(value, 10),
                > 0 => numService.Increment(value, 10),
                _ => value
            };

            value = e.Delta.X switch
            {
                < 0 => numService.Decrement(value, 1),
                > 0 => numService.Increment(value, 1),
                _ => value
            };

            vm.Animation.Previous = numService.Clamp(value, numService.GetMinimum(vm.Setter), numService.GetMaximum(vm.Setter));

            e.Handled = true;
        }
    }

    private void NextTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not AnimationEditorViewModel<T> vm ||
            vm.Description.NumberEditorService is not INumberEditorService<T> numService)
        {
            return;
        }

        if (nextTextBox.IsKeyboardFocusWithin && numService.TryParse(nextTextBox.Text, out T value))
        {
            value = e.Delta.Y switch
            {
                < 0 => numService.Decrement(value, 10),
                > 0 => numService.Increment(value, 10),
                _ => value
            };

            value = e.Delta.X switch
            {
                < 0 => numService.Decrement(value, 1),
                > 0 => numService.Increment(value, 1),
                _ => value
            };

            vm.Animation.Next = numService.Clamp(value, numService.GetMinimum(vm.Setter), numService.GetMaximum(vm.Setter));

            e.Handled = true;
        }
    }
}
