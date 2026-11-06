using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.VisualTree;
using Beutl.Editor.Components.Helpers;
using Beutl.ViewModels;
using FluentAvalonia.UI.Controls;

namespace Beutl.Views;

// An Edit entry of the macOS menu bar. AppKit gives a key equivalent to the menu before the focused control sees the
// key, which would take Cmd+Z and Cmd+C from a text box. So the entry hands its key to the focused control the way the
// key goes on the other platforms, where a command bound to it can take it too, and fallback does what none took.
internal sealed class FocusedEditCommand(StandardEditAction action, ICommand fallback, TopLevel window, KeyGesture? shownGesture = null) : ICommand
{
    private EventHandler? _focusChanged;

    public event EventHandler? CanExecuteChanged
    {
        add
        {
            fallback.CanExecuteChanged += value;
            _focusChanged += value;
        }
        remove
        {
            fallback.CanExecuteChanged -= value;
            _focusChanged -= value;
        }
    }

    public ICommand Fallback => fallback;

    // Test seam: the Headless platform has no AppKit event to look at.
    internal Func<bool> IsKeyEquivalent { get; init; } = () => IsCurrentEventKeyDown(window);

    // A text input takes the action whether or not the fallback, which may need a scene, is available.
    public bool CanExecute(object? parameter)
        => fallback.CanExecute(parameter) || IsTextInput(window.FocusManager?.GetFocusedElement());

    // Keeps the entry's state with the focus until disposed.
    internal IDisposable FollowFocus() => new CompositeDisposable(
        window.AddDisposableHandler(InputElement.GotFocusEvent, (_, _) => _focusChanged?.Invoke(this, EventArgs.Empty), handledEventsToo: true),
        window.AddDisposableHandler(InputElement.LostFocusEvent, (_, _) => _focusChanged?.Invoke(this, EventArgs.Empty), handledEventsToo: true));

    public void Execute(object? parameter)
    {
        bool isDialogOpen = IsDialogOpen(window);
        var target = window.FocusManager?.GetFocusedElement() as Interactive ?? window;
        // Not every dialog takes the focus when it opens, and what it covers must not get the key.
        if (isDialogOpen && (target as Visual)?.FindAncestorOfType<FAContentDialog>(includeSelf: true) is null)
            return;

        if (!isDialogOpen && SendToNativeResponder())
            return;

        bool isKey = IsKeyEquivalent();
        bool isTextInput = IsTextInput(target);
        // A text input knows only the standard keys. Elsewhere a key map can have moved the entry's key, and the
        // standard one may then run another command.
        KeyGesture gesture = (isTextInput ? null : shownGesture) ?? GetKeyGesture(action);
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = gesture.Key,
            KeyModifiers = gesture.KeyModifiers,
            Source = target
        };
        target.RaiseEvent(args);

        if (args.Handled || isDialogOpen || isTextInput)
            return;

        // Cut, Copy and Paste keys belong to the panel with the focus, which may have nothing to do with the
        // selected clip. Choosing the entry names the clip, and Undo acts on the editor from anywhere.
        if (!isKey || action is StandardEditAction.Undo or StandardEditAction.Redo)
            fallback.Execute(parameter);
    }

    // The macOS menu bar stays reachable while a dialog covers the window, unlike keys that bubble to the main view.
    internal static bool IsDialogOpen(TopLevel window)
        => OverlayLayer.GetOverlayLayer(window)?.GetVisualDescendants().OfType<FAContentDialog>().Any() == true;

    private static bool IsTextInput(object? element)
        => ContextCommandInput.IsFromTextInput(new KeyEventArgs { Source = element });

    internal static KeyGesture GetKeyGesture(StandardEditAction action)
    {
        KeyModifiers command = KeyGestureHelper.GetCommandModifier();
        return action switch
        {
            StandardEditAction.Undo => new KeyGesture(Key.Z, command),
            StandardEditAction.Redo => new KeyGesture(Key.Z, command | KeyModifiers.Shift),
            StandardEditAction.Cut => new KeyGesture(Key.X, command),
            StandardEditAction.Copy => new KeyGesture(Key.C, command),
            StandardEditAction.Paste => new KeyGesture(Key.V, command),
            _ => new KeyGesture(Key.A, command)
        };
    }

    // A native view with the first responder, such as the browser tab's web view, does the action itself.
    private bool SendToNativeResponder()
    {
        if (window.TryGetPlatformHandle() is not IMacOSTopLevelPlatformHandle handle)
            return false;

        nint responder = ObjCRuntime.Send(handle.NSWindow, ObjCRuntime.Selector("firstResponder"));
        if (responder == 0 || responder == handle.NSView || responder == handle.NSWindow)
            return false;

        string selector = action switch
        {
            StandardEditAction.Undo => "undo:",
            StandardEditAction.Redo => "redo:",
            StandardEditAction.Cut => "cut:",
            StandardEditAction.Copy => "copy:",
            StandardEditAction.Paste => "paste:",
            _ => "selectAll:"
        };
        // No target: AppKit walks the responder chain from the key window's first responder, as for any Edit menu.
        return ObjCRuntime.SendBool(
            SharedApplication(), ObjCRuntime.Selector("sendAction:to:from:"), ObjCRuntime.Selector(selector), 0, 0);
    }

    private static bool IsCurrentEventKeyDown(TopLevel window)
    {
        const nint NSEventTypeKeyDown = 10;
        if (window.TryGetPlatformHandle() is not IMacOSTopLevelPlatformHandle)
            return false;

        nint currentEvent = ObjCRuntime.Send(SharedApplication(), ObjCRuntime.Selector("currentEvent"));
        return currentEvent != 0 && ObjCRuntime.Send(currentEvent, ObjCRuntime.Selector("type")) == NSEventTypeKeyDown;
    }

    private static nint SharedApplication()
        => ObjCRuntime.Send(ObjCRuntime.GetClass("NSApplication"), ObjCRuntime.Selector("sharedApplication"));
}
