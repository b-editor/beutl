using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using BEditorNext.Services.Editors;
using BEditorNext.ViewModels;
using BEditorNext.ViewModels.AnimationEditors;

namespace BEditorNext.Views.AnimationEditors;

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

    private bool PrevTryParse(INumberEditorService<T> service, out T value)
    {
        bool result = service.TryParse(prevTextBox.Text, out value);
        SetPrevError(!result);
        return result;
    }

    private bool NextTryParse(INumberEditorService<T> service, out T value)
    {
        bool result = service.TryParse(nextTextBox.Text, out value);
        SetNextError(!result);
        return result;
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
            vm.EditorViewModel is not INumberEditorViewModel<T> numVM)
        {
            return;
        }

        if (PrevTryParse(numVM.EditorService, out T newValue))
        {
            vm.SetPrevious(_oldPrev, newValue);
        }
    }

    private void NextTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AnimationEditorViewModel<T> vm ||
            vm.EditorViewModel is not INumberEditorViewModel<T> numVM)
        {
            return;
        }

        if (NextTryParse(numVM.EditorService, out T newValue))
        {
            vm.SetNext(_oldNext, newValue);
        }
    }

    private void PreviousTextBox_TextChanged(string s)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (DataContext is not AnimationEditorViewModel<T> vm ||
                vm.EditorViewModel is not INumberEditorViewModel<T> numVM)
            {
                return;
            }

            await Task.Delay(10);

            if (PrevTryParse(numVM.EditorService, out T value))
            {
                vm.Animation.Previous = numVM.EditorService.Clamp(value, numVM.Minimum, numVM.Maximum);
            }
        });
    }

    private void NextTextBox_TextChanged(string s)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (DataContext is not AnimationEditorViewModel<T> vm ||
                vm.EditorViewModel is not INumberEditorViewModel<T> numVM)
            {
                return;
            }

            await Task.Delay(10);

            if (NextTryParse(numVM.EditorService, out T value))
            {
                vm.Animation.Next = numVM.EditorService.Clamp(value, numVM.Minimum, numVM.Maximum);
            }
        });
    }

    private void PreviousTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not AnimationEditorViewModel<T> vm ||
            vm.EditorViewModel is not INumberEditorViewModel<T> numVM)
        {
            return;
        }

        if (prevTextBox.IsKeyboardFocusWithin && PrevTryParse(numVM.EditorService, out T value))
        {
            int increment = 10;

            if (e.KeyModifiers == KeyModifiers.Shift)
            {
                increment = 1;
            }

            if (e.Delta.Y < 0)
            {
                value = numVM.EditorService.Decrement(value, increment);
            }
            else
            {
                value = numVM.EditorService.Increment(value, increment);
            }

            vm.Animation.Previous = numVM.EditorService.Clamp(value, numVM.Minimum, numVM.Maximum);

            e.Handled = true;
        }
    }

    private void NextTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not AnimationEditorViewModel<T> vm ||
            vm.EditorViewModel is not INumberEditorViewModel<T> numVM)
        {
            return;
        }

        if (nextTextBox.IsKeyboardFocusWithin && NextTryParse(numVM.EditorService, out T value))
        {
            int increment = 10;

            if (e.KeyModifiers == KeyModifiers.Shift)
            {
                increment = 1;
            }

            if (e.Delta.Y < 0)
            {
                value = numVM.EditorService.Decrement(value, increment);
            }
            else
            {
                value = numVM.EditorService.Increment(value, increment);
            }

            vm.Animation.Next = numVM.EditorService.Clamp(value, numVM.Minimum, numVM.Maximum);

            e.Handled = true;
        }
    }

    private void SetPrevError(bool state)
    {
        (prevTextBox.Classes as IPseudoClasses).Set(":error", state);
    }

    private void SetNextError(bool state)
    {
        (nextTextBox.Classes as IPseudoClasses).Set(":error", state);
    }
}
