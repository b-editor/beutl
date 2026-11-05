using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Beutl.Editor.Components.Helpers;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;
using Dock.Avalonia.Controls;

namespace Beutl.Helpers;

// The keys every macOS app has that Avalonia leaves out.
internal static class MacOSKeyBindings
{
    public static void Install(Application application)
    {
        if (!OperatingSystem.IsMacOS())
            return;

        if (application.PlatformSettings?.HotkeyConfiguration is { } hotkeys)
            AddTextNavigation(hotkeys);
        if (NativeMenu.GetMenu(application) is { } menu)
            FixHideOthersGesture(menu);

        InputElement.KeyDownEvent.AddClassHandler<TextBox>(OnTextBoxKeyDown, RoutingStrategies.Tunnel);
        InputElement.KeyDownEvent.AddClassHandler<Window>(CloseSecondaryWindow);
    }

    internal static void AddTextNavigation(PlatformHotkeyConfiguration hotkeys)
    {
        hotkeys.MoveCursorToTheStartOfDocument.Add(new KeyGesture(Key.Up, KeyModifiers.Meta));
        hotkeys.MoveCursorToTheEndOfDocument.Add(new KeyGesture(Key.Down, KeyModifiers.Meta));
        hotkeys.MoveCursorToTheStartOfDocumentWithSelection.Add(new KeyGesture(Key.Up, KeyModifiers.Meta | KeyModifiers.Shift));
        hotkeys.MoveCursorToTheEndOfDocumentWithSelection.Add(new KeyGesture(Key.Down, KeyModifiers.Meta | KeyModifiers.Shift));
        hotkeys.MoveCursorToTheStartOfLine.Add(new KeyGesture(Key.A, KeyModifiers.Control));
        hotkeys.MoveCursorToTheEndOfLine.Add(new KeyGesture(Key.E, KeyModifiers.Control));
    }

    // Avalonia puts Hide Others on Cmd+Alt+Q in the application menu it fills in.
    internal static void FixHideOthersGesture(NativeMenu applicationMenu)
    {
        var misplaced = new KeyGesture(Key.Q, KeyModifiers.Meta | KeyModifiers.Alt);
        if (applicationMenu.Items.OfType<NativeMenuItem>().FirstOrDefault(item => misplaced.Equals(item.Gesture)) is { } hideOthers)
            hideOthers.Gesture = new KeyGesture(Key.H, KeyModifiers.Meta | KeyModifiers.Alt);
    }

    // The text commands of macOS that TextBox has no list of gestures for, in terms of the keys it does have.
    internal static void OnTextBoxKeyDown(TextBox textBox, KeyEventArgs e)
    {
        switch (e.KeyModifiers, e.Key)
        {
            case (KeyModifiers.Meta, Key.Back):
                DeleteTo(textBox, e, Key.Home, Key.Back);
                break;
            case (KeyModifiers.Control, Key.K):
                DeleteTo(textBox, e, Key.End, Key.Delete);
                break;
            case (KeyModifiers.Control, Key.D):
                Press(textBox, e, Key.Delete);
                break;
            case (KeyModifiers.Control, Key.H):
                Press(textBox, e, Key.Back);
                break;
            case (KeyModifiers.Control, Key.F):
                Press(textBox, e, Key.Right);
                break;
            case (KeyModifiers.Control, Key.B):
                Press(textBox, e, Key.Left);
                break;
            case (KeyModifiers.Control, Key.N):
                Press(textBox, e, Key.Down);
                break;
            case (KeyModifiers.Control, Key.P):
                Press(textBox, e, Key.Up);
                break;
        }
    }

    // A selection is what gets deleted, as with any other delete key.
    private static void DeleteTo(TextBox textBox, KeyEventArgs e, Key lineEnd, Key delete)
    {
        if (textBox.SelectionStart == textBox.SelectionEnd)
            Press(textBox, e, lineEnd, KeyModifiers.Shift);
        Press(textBox, e, delete);
    }

    private static void Press(TextBox textBox, KeyEventArgs e, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        e.Handled = true;
        textBox.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
            Source = textBox
        });
    }

    // Only the main window has a menu bar with Close Tab. The others show just the application menu, so the key
    // reaches them as a key. A floating dock window closes its focused tab, and the dock closes it with the last one.
    internal static void CloseSecondaryWindow(Window window, KeyEventArgs e)
    {
        if (window.DataContext is MainViewModel
            || GetCommandGestureExtension.GetKeyGesture(typeof(MainViewExtension), MainViewExtension.CloseTabCommandId)?.Matches(e) != true)
        {
            return;
        }

        e.Handled = true;
        if (window is HostWindow { Window: { Factory: { } factory, Layout.FocusedDockable: { CanClose: true } focused } })
            factory.CloseDockable(focused);
        else
            window.Close();
    }
}
