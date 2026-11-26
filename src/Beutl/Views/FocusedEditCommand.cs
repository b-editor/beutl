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
using ObjCRuntime = Beutl.Controls.ObjCRuntime;

namespace Beutl.Views;

// An Edit entry that lets the focused text input handle its standard gesture. AppKit can also give a key equivalent
// to the native menu before the focused control sees the key, so the native entry hands that key on to other controls too.
internal sealed class FocusedEditCommand(StandardEditAction action, ICommand fallback, Control owner, KeyGesture? shownGesture = null) : ICommand
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

    private TopLevel? Window => owner as TopLevel ?? TopLevel.GetTopLevel(owner);

    private Interactive? FocusedElement
        => InWindowFocus?.Target ?? Window?.FocusManager?.GetFocusedElement() as Interactive;

    // The key the entry shows, which the key map can move.
    internal KeyGesture? ShownGesture { get; set; } = shownGesture;

    // Test seam: the Headless platform has no AppKit event to look at.
    internal Func<bool> IsKeyEquivalent { get; init; } = () => owner is TopLevel window && IsCurrentEventKeyDown(window);

    internal InWindowEditMenuFocus? InWindowFocus { get; init; }

    internal (Func<bool> IsEnabled, IObservable<Unit> Changed)? Availability { get; init; }

    // A text input takes the action whether or not the fallback, which may need a scene, is available.
    public bool CanExecute(object? parameter)
        => Availability?.IsEnabled() != false
            && (fallback.CanExecute(parameter) || IsTextInput(FocusedElement));

    // Keeps the entry's state with the focus and its availability until disposed.
    internal IDisposable FollowFocus() => new CompositeDisposable(
        owner.AddDisposableHandler(InputElement.GotFocusEvent, (_, _) => _focusChanged?.Invoke(this, EventArgs.Empty), handledEventsToo: true),
        owner.AddDisposableHandler(InputElement.LostFocusEvent, (_, _) => _focusChanged?.Invoke(this, EventArgs.Empty), handledEventsToo: true),
        Availability?.Changed.Subscribe(_ => _focusChanged?.Invoke(this, EventArgs.Empty)) ?? Disposable.Empty);

    public void Execute(object? parameter)
    {
        if (Availability?.IsEnabled() == false || Window is not { } window)
            return;

        bool isDialogOpen = IsDialogOpen(window);
        var target = FocusedElement ?? window;
        // Not every dialog takes the focus when it opens, and what it covers must not get the key.
        if (isDialogOpen && (target as Visual)?.FindAncestorOfType<FAContentDialog>(includeSelf: true) is null)
            return;

        if (InWindowFocus is { } menuFocus)
        {
            if (menuFocus.Target is not null || IsTextInput(target))
            {
                if (target is InputElement { IsEffectivelyVisible: true, IsEffectivelyEnabled: true }
                    && TopLevel.GetTopLevel(target) == window)
                {
                    menuFocus.RestoreSelection();
                    SendKey(target, GetKeyGesture(action));
                }
            }
            else if (!isDialogOpen && fallback.CanExecute(parameter))
            {
                fallback.Execute(parameter);
            }

            return;
        }

        if (!isDialogOpen && SendToNativeResponder(window))
            return;

        // Delete has no key equivalent, even when the menu item is chosen with the keyboard.
        bool isKey = action != StandardEditAction.Delete && IsKeyEquivalent();
        bool isTextInput = IsTextInput(target);
        // A text input knows only the standard keys. Elsewhere a key map can have moved the entry's key, and the
        // standard one may then run another command.
        KeyGesture gesture = (isTextInput ? null : ShownGesture) ?? GetKeyGesture(action);
        if (SendKey(target, gesture) || isDialogOpen || isTextInput)
            return;

        // Cut, Copy and Paste keys belong to the panel with the focus, which may have nothing to do with the
        // selected clip. Choosing the entry names the clip, and Undo acts on the editor from anywhere.
        if (!isKey || action is StandardEditAction.Undo or StandardEditAction.Redo)
            fallback.Execute(parameter);
    }

    // The macOS menu bar stays reachable while a dialog covers the window, unlike keys that bubble to the main view.
    internal static bool IsDialogOpen(TopLevel window)
        => OverlayLayer.GetOverlayLayer(window)?.GetVisualDescendants().OfType<FAContentDialog>().Any() == true;

    internal static bool IsTextInput(object? element)
        => ContextCommandInput.IsFromTextInput(new KeyEventArgs { Source = element });

    private static bool SendKey(Interactive target, KeyGesture gesture)
    {
        var args = new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = gesture.Key,
            KeyModifiers = gesture.KeyModifiers,
            Source = target
        };
        target.RaiseEvent(args);
        return args.Handled;
    }

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
            StandardEditAction.Delete => new KeyGesture(Key.Delete),
            _ => new KeyGesture(Key.A, command)
        };
    }

    // A native view with the first responder, such as the browser tab's web view, does the action itself.
    private bool SendToNativeResponder(TopLevel window)
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
            StandardEditAction.Delete => "delete:",
            _ => "selectAll:"
        };
        // No target: AppKit walks the responder chain from the key window's first responder, as for any Edit menu.
        return ObjCRuntime.SendBool(
            SharedApplication(), ObjCRuntime.Selector("sendAction:to:from:"), ObjCRuntime.Selector(selector), 0, 0);
    }

    internal static bool IsCurrentEventKeyDown(TopLevel window)
    {
        const nint NSEventTypeKeyDown = 10;
        if (window.TryGetPlatformHandle() is not IMacOSTopLevelPlatformHandle)
            return false;

        nint currentEvent = ObjCRuntime.CurrentEvent();
        return currentEvent != 0 && ObjCRuntime.Send(currentEvent, ObjCRuntime.Selector("type")) == NSEventTypeKeyDown;
    }

    private static nint SharedApplication()
        => ObjCRuntime.Send(ObjCRuntime.GetClass("NSApplication"), ObjCRuntime.Selector("sharedApplication"));
}
