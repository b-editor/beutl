using System.Reactive.Disposables;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

using Beutl.Reactive;

namespace Beutl.Controls.PropertyEditors;

internal readonly struct ComponentValueHandlers(
    EventHandler<FocusChangedEventArgs> gotFocus,
    EventHandler<FocusChangedEventArgs> lostFocus,
    Action<TextBox, string?, string?> textChanged,
    EventHandler<PointerWheelEventArgs> pointerWheelChanged)
{
    public void Subscribe(TextBox? textBox, CompositeDisposable disposables)
    {
        if (textBox != null)
        {
            // A struct cannot capture its primary constructor parameters in a lambda.
            Action<TextBox, string?, string?> onTextChanged = textChanged;
            textBox.AddDisposableHandler(InputElement.GotFocusEvent, gotFocus)
                .DisposeWith(disposables);
            textBox.AddDisposableHandler(InputElement.LostFocusEvent, lostFocus)
                .DisposeWith(disposables);
            textBox.GetPropertyChangedObservable(TextBox.TextProperty)
                .Subscribe(e =>
                {
                    if (e is AvaloniaPropertyChangedEventArgs<string> args
                        && args.Sender is TextBox textBox)
                    {
                        onTextChanged(textBox, args.NewValue.GetValueOrDefault(), args.OldValue.GetValueOrDefault());
                    }
                })
                .DisposeWith(disposables);
            textBox.AddDisposableHandler(InputElement.PointerWheelChangedEvent, pointerWheelChanged, RoutingStrategies.Tunnel)
                .DisposeWith(disposables);
        }
    }
}

internal readonly struct ComponentHoverHandlers(
    EventHandler<FocusChangedEventArgs> gotFocus,
    EventHandler<FocusChangedEventArgs> lostFocus,
    Action<bool> pointerOverChanged)
{
    public void SubscribeTextBox(TextBox? textBox, CompositeDisposable disposables)
    {
        if (textBox != null)
        {
            textBox.AddDisposableHandler(InputElement.GotFocusEvent, gotFocus)
                .DisposeWith(disposables);
            textBox.AddDisposableHandler(InputElement.LostFocusEvent, lostFocus)
                .DisposeWith(disposables);
            textBox.GetObservable(InputElement.IsPointerOverProperty)
                .Subscribe(pointerOverChanged)
                .DisposeWith(disposables);
        }
    }

    public void SubscribePointerOver(Control? control, CompositeDisposable disposables)
    {
        control?.GetObservable(InputElement.IsPointerOverProperty)
            ?.Subscribe(pointerOverChanged)
            ?.DisposeWith(disposables);
    }
}

internal readonly struct ScrubHeaderHandlers(
    EventHandler<PointerPressedEventArgs> pressed,
    EventHandler<PointerReleasedEventArgs> released,
    EventHandler<PointerEventArgs> moved,
    EventHandler<PointerCaptureLostEventArgs> captureLost,
    RoutingStrategies routes)
{
    public void Subscribe(TextBlock? header, CompositeDisposable disposables)
    {
        if (header != null)
        {
            header.AddDisposableHandler(InputElement.PointerPressedEvent, pressed, routes)
                .DisposeWith(disposables);
            header.AddDisposableHandler(InputElement.PointerReleasedEvent, released, routes)
                .DisposeWith(disposables);
            header.AddDisposableHandler(InputElement.PointerMovedEvent, moved, routes)
                .DisposeWith(disposables);
            // Raised directly on each element that loses the capture, including when it leaves the tree.
            header.AddDisposableHandler(InputElement.PointerCaptureLostEvent, captureLost, RoutingStrategies.Direct)
                .DisposeWith(disposables);
            header.Cursor = PointerLockHelper.SizeWestEast;
        }
    }
}
