using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using BEditorNext.ViewModels.AnimationEditors;

namespace BEditorNext.Views.AnimationEditors;

public partial class BaseVector2AnimationEditor : UserControl
{
    public BaseVector2AnimationEditor()
    {
        InitializeComponent();
    }

    private void Slide_Click(object? sender, RoutedEventArgs e)
    {
        if (carousel.SelectedIndex == 0)
        {
            carousel.Next();
            button_icon.IconType = Controls.FluentIconsRegular.Chevron_Left;
        }
        else
        {
            carousel.Previous();
            button_icon.IconType = Controls.FluentIconsRegular.Chevron_Right;
        }
    }
}

public abstract class BaseVector2AnimationEditor<T> : BaseVector2AnimationEditor
    where T : struct
{
    private T _oldPrev;
    private T _oldNext;

    protected BaseVector2AnimationEditor()
    {
        void PrevAddHandlers(TextBox textBox)
        {
            textBox.GotFocus += PreviousTextBox_GotFocus;
            textBox.LostFocus += PreviousTextBox_LostFocus;
            textBox.GetObservable(TextBox.TextProperty).Subscribe(PreviousTextBox_TextChanged);
        }
        void NextAddHandlers(TextBox textBox)
        {
            textBox.GotFocus += NextTextBox_GotFocus;
            textBox.LostFocus += NextTextBox_LostFocus;
            textBox.GetObservable(TextBox.TextProperty).Subscribe(NextTextBox_TextChanged);
        }

        PrevAddHandlers(prevXTextBox);
        PrevAddHandlers(prevYTextBox);

        NextAddHandlers(nextXTextBox);
        NextAddHandlers(nextYTextBox);

        prevXTextBox.AddHandler(PointerWheelChangedEvent, PreviousXTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
        prevYTextBox.AddHandler(PointerWheelChangedEvent, PreviousYTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
        nextXTextBox.AddHandler(PointerWheelChangedEvent, NextXTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
        nextYTextBox.AddHandler(PointerWheelChangedEvent, NextYTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
    }

    protected abstract bool TryParse(string? x, string? y, out T value);

    protected abstract T Clamp(T value);

    protected abstract T IncrementX(T value, int increment);

    protected abstract T IncrementY(T value, int increment);

    private bool PrevTryParse(out T value)
    {
        return TryParse(prevXTextBox.Text, prevYTextBox.Text, out value);
    }

    private bool NextTryParse(out T value)
    {
        return TryParse(nextXTextBox.Text, nextYTextBox.Text, out value);
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
        if (DataContext is not AnimationEditorViewModel<T> vm) return;

        if (PrevTryParse(out T newValue))
        {
            vm.SetPrevious(_oldPrev, newValue);
        }
    }

    private void NextTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AnimationEditorViewModel<T> vm) return;

        if (NextTryParse(out T newValue))
        {
            vm.SetNext(_oldNext, newValue);
        }
    }

    private void PreviousTextBox_TextChanged(string s)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (DataContext is not AnimationEditorViewModel<T> vm) return;

            await Task.Delay(10);

            if (PrevTryParse(out T value))
            {
                vm.Animation.Previous = Clamp(value);
            }
        });
    }

    private void NextTextBox_TextChanged(string s)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (DataContext is not AnimationEditorViewModel<T> vm) return;

            await Task.Delay(10);

            if (NextTryParse(out T value))
            {
                vm.Animation.Next = Clamp(value);
            }
        });
    }

    private void PreviousXTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        OnPrevPointerWheelChanged(sender, e, IncrementX);
    }

    private void PreviousYTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        OnPrevPointerWheelChanged(sender, e, IncrementY);
    }

    private void NextXTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        OnNextPointerWheelChanged(sender, e, IncrementX);
    }

    private void NextYTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        OnNextPointerWheelChanged(sender, e, IncrementY);
    }

    private void OnPrevPointerWheelChanged(object? sender, PointerWheelEventArgs e, Func<T, int, T> func)
    {
        if (DataContext is not AnimationEditorViewModel<T> vm || sender is not TextBox textBox) return;

        if (textBox.IsKeyboardFocusWithin && PrevTryParse(out T value))
        {
            int increment = 10;

            if (e.KeyModifiers == KeyModifiers.Shift)
            {
                increment = 1;
            }

            value = func(value, (e.Delta.Y < 0) ? -increment : increment);

            vm.Animation.Previous = Clamp(value);

            e.Handled = true;
        }
    }

    private void OnNextPointerWheelChanged(object? sender, PointerWheelEventArgs e, Func<T, int, T> func)
    {
        if (DataContext is not AnimationEditorViewModel<T> vm || sender is not TextBox textBox) return;

        if (textBox.IsKeyboardFocusWithin && NextTryParse(out T value))
        {
            int increment = 10;

            if (e.KeyModifiers == KeyModifiers.Shift)
            {
                increment = 1;
            }

            value = func(value, (e.Delta.Y < 0) ? -increment : increment);

            vm.Animation.Next = Clamp(value);

            e.Handled = true;
        }
    }
}
