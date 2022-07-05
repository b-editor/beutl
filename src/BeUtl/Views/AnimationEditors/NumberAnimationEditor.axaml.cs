using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

using BeUtl.Services.Editors;
using BeUtl.Services.Editors.Wrappers;
using BeUtl.Streaming;
using BeUtl.ViewModels.AnimationEditors;

namespace BeUtl.Views.AnimationEditors;

public partial class NumberAnimationEditor : UserControl
{
    public NumberAnimationEditor()
    {
        InitializeComponent();
    }
}

public sealed class NumberAnimationEditor<T> : NumberAnimationEditor
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

    private static bool TryParse(INumberEditorService<T> service, IWrappedProperty<T> property, string s, out T result)
    {
        bool parsed;
        if (property is SetterDescription<T>.InternalSetter { Description.Parser: { } parser })
        {
            (result, parsed) = parser(s);
            return parsed;
        }
        else
        {
            return service.TryParse(s, out result);
        }
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
        if (DataContext is AnimationEditorViewModel<T>
            {
                Description.NumberEditorService: INumberEditorService<T> service,
                WrappedProperty: { } property
            } vm
            && NumberAnimationEditor<T>.TryParse(service, property, prevTextBox.Text, out T newValue))
        {
            vm.SetPrevious(_oldPrev, newValue);
        }
    }

    private void NextTextBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AnimationEditorViewModel<T>
            {
                Description.NumberEditorService: INumberEditorService<T> service,
                WrappedProperty: { } property
            } vm
            && NumberAnimationEditor<T>.TryParse(service, property, nextTextBox.Text, out T newValue))
        {
            vm.SetNext(_oldNext, newValue);
        }
    }

    private void PreviousTextBox_TextChanged(string s)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Delay(10);

            if (DataContext is AnimationEditorViewModel<T>
                {
                    Description.NumberEditorService: INumberEditorService<T> service,
                    WrappedProperty: { } property
                } vm
                && NumberAnimationEditor<T>.TryParse(service, property, prevTextBox.Text, out T value))
            {
                vm.Animation.Previous = service.Clamp(value, service.GetMinimum(property), service.GetMaximum(property));
            }
        });
    }

    private void NextTextBox_TextChanged(string s)
    {
        Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Delay(10);

            if (DataContext is AnimationEditorViewModel<T>
                {
                    Description.NumberEditorService: INumberEditorService<T> service,
                    WrappedProperty: { } property
                } vm
                && NumberAnimationEditor<T>.TryParse(service, property, nextTextBox.Text, out T value))
            {
                vm.Animation.Next = service.Clamp(value, service.GetMinimum(property), service.GetMaximum(property));
            }
        });
    }

    private void PreviousTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is AnimationEditorViewModel<T>
            {
                Description.NumberEditorService: INumberEditorService<T> service,
                WrappedProperty: { } property
            } vm
            && prevTextBox.IsKeyboardFocusWithin
            && NumberAnimationEditor<T>.TryParse(service, property, prevTextBox.Text, out T value))
        {
            value = e.Delta.Y switch
            {
                < 0 => service.Decrement(value, 10),
                > 0 => service.Increment(value, 10),
                _ => value
            };

            value = e.Delta.X switch
            {
                < 0 => service.Decrement(value, 1),
                > 0 => service.Increment(value, 1),
                _ => value
            };

            vm.Animation.Previous = service.Clamp(value, service.GetMinimum(property), service.GetMaximum(property));

            e.Handled = true;
        }
    }

    private void NextTextBox_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is AnimationEditorViewModel<T>
            {
                Description.NumberEditorService: INumberEditorService<T> service,
                WrappedProperty: { } property
            } vm
            && nextTextBox.IsKeyboardFocusWithin
            && NumberAnimationEditor<T>.TryParse(service, property, nextTextBox.Text, out T value))
        {
            value = e.Delta.Y switch
            {
                < 0 => service.Decrement(value, 10),
                > 0 => service.Increment(value, 10),
                _ => value
            };

            value = e.Delta.X switch
            {
                < 0 => service.Decrement(value, 1),
                > 0 => service.Increment(value, 1),
                _ => value
            };

            vm.Animation.Next = service.Clamp(value, service.GetMinimum(property), service.GetMaximum(property));

            e.Handled = true;
        }
    }
}
