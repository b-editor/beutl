
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using BeUtl.ViewModels.AnimationEditors;

namespace BeUtl.Views.AnimationEditors;

public partial class BaseVector4AnimationEditor : UserControl
{
    public BaseVector4AnimationEditor()
    {
        InitializeComponent();
    }
}

public abstract class BaseVector4AnimationEditor<T> : BaseVector4AnimationEditor
    where T : struct
{
    private T _oldPrev;
    private T _oldNext;

    protected BaseVector4AnimationEditor()
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
        PrevAddHandlers(prevZTextBox);
        PrevAddHandlers(prevWTextBox);

        NextAddHandlers(nextXTextBox);
        NextAddHandlers(nextYTextBox);
        NextAddHandlers(nextZTextBox);
        NextAddHandlers(nextWTextBox);

        prevXTextBox.AddHandler(PointerWheelChangedEvent, PreviousXTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
        prevYTextBox.AddHandler(PointerWheelChangedEvent, PreviousYTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
        prevZTextBox.AddHandler(PointerWheelChangedEvent, PreviousZTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
        prevWTextBox.AddHandler(PointerWheelChangedEvent, PreviousWTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
        nextXTextBox.AddHandler(PointerWheelChangedEvent, NextXTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
        nextYTextBox.AddHandler(PointerWheelChangedEvent, NextYTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
        nextZTextBox.AddHandler(PointerWheelChangedEvent, NextZTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
        nextWTextBox.AddHandler(PointerWheelChangedEvent, NextWTextBox_PointerWheelChanged, RoutingStrategies.Tunnel);
    }

    protected abstract bool TryParse(string? x, string? y, string? z, string? w, out T value);

    protected abstract T Clamp(T value);

    protected abstract T IncrementX(T value, int increment);

    protected abstract T IncrementY(T value, int increment);

    protected abstract T IncrementZ(T value, int increment);

    protected abstract T IncrementW(T value, int increment);

    private bool PrevTryParse(out T value)
    {
        return TryParse(prevXTextBox.Text, prevYTextBox.Text, prevZTextBox.Text, prevWTextBox.Text, out value);
    }

    private bool NextTryParse(out T value)
    {
        return TryParse(nextXTextBox.Text, nextYTextBox.Text, nextZTextBox.Text, nextWTextBox.Text, out value);
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

    private void PreviousZTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        OnPrevPointerWheelChanged(sender, e, IncrementZ);
    }

    private void PreviousWTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        OnPrevPointerWheelChanged(sender, e, IncrementW);
    }

    private void NextXTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        OnNextPointerWheelChanged(sender, e, IncrementX);
    }

    private void NextYTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        OnNextPointerWheelChanged(sender, e, IncrementY);
    }

    private void NextZTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        OnNextPointerWheelChanged(sender, e, IncrementZ);
    }

    private void NextWTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        OnNextPointerWheelChanged(sender, e, IncrementW);
    }

    private void OnPrevPointerWheelChanged(object? sender, PointerWheelEventArgs e, Func<T, int, T> func)
    {
        if (DataContext is not AnimationEditorViewModel<T> vm || sender is not TextBox textBox) return;

        if (textBox.IsKeyboardFocusWithin && PrevTryParse(out T value))
        {
            value = e.Delta.Y switch
            {
                < 0 => func(value, -10),
                > 0 => func(value, 10),
                _ => value
            };

            value = e.Delta.X switch
            {
                < 0 => func(value, -1),
                > 0 => func(value, 1),
                _ => value
            };

            vm.Animation.Previous = Clamp(value);

            e.Handled = true;
        }
    }

    private void OnNextPointerWheelChanged(object? sender, PointerWheelEventArgs e, Func<T, int, T> func)
    {
        if (DataContext is not AnimationEditorViewModel<T> vm || sender is not TextBox textBox) return;

        if (textBox.IsKeyboardFocusWithin && NextTryParse(out T value))
        {
            value = e.Delta.Y switch
            {
                < 0 => func(value, -10),
                > 0 => func(value, 10),
                _ => value
            };

            value = e.Delta.X switch
            {
                < 0 => func(value, -1),
                > 0 => func(value, 1),
                _ => value
            };

            vm.Animation.Next = Clamp(value);

            e.Handled = true;
        }
    }
}
