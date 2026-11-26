using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Beutl.Editor.Components.Helpers;

// A panning shortcut (Space by default) that still reaches the outer keymap when tapped.
// Only its release can tell the two apart, so the owner keeps the press from the key map until then.
internal sealed class SpacePanKey
{
    private readonly Control _owner;
    private Key? _pressedKey;
    // What the held key was pressed with, until a pointer press makes the hold a gesture rather than a tap.
    private KeyModifiers? _tap;

    public SpacePanKey(Control owner)
    {
        _owner = owner;
        owner.AddHandler(InputElement.PointerPressedEvent, (_, _) => _tap = null, RoutingStrategies.Tunnel);
    }

    public bool IsPressed => _pressedKey.HasValue;

    public bool HandleRepeat(KeyEventArgs e)
    {
        if (_pressedKey != e.Key) return false;
        e.Handled = true;
        return true;
    }

    // canTap is false during a pointer gesture. A held key repeats its press, which changes nothing.
    public void Press(KeyEventArgs e, bool canTap)
    {
        if (IsPressed) return;
        _pressedKey = e.Key;
        if (canTap) _tap = e.KeyModifiers;
    }

    public bool Release(KeyEventArgs e)
    {
        if (_pressedKey != e.Key) return false;
        KeyModifiers? tap = _tap;
        Reset();
        if (tap is { } modifiers) ReplayTap(e, modifiers);
        return true;
    }

    public void Reset()
    {
        _pressedKey = null;
        _tap = null;
    }

    // The tap starts past the owner, which has already had the key. The release follows the press because
    // handlers on the way pair them, like the key monitor's on the window.
    private void ReplayTap(KeyEventArgs released, KeyModifiers modifiers)
    {
        if (_owner.Parent is not Interactive parent) return;
        foreach (RoutedEvent<KeyEventArgs> routedEvent in new[] { InputElement.KeyDownEvent, InputElement.KeyUpEvent })
        {
            parent.RaiseEvent(new KeyEventArgs
            {
                RoutedEvent = routedEvent,
                Key = released.Key,
                KeyModifiers = modifiers,
                PhysicalKey = released.PhysicalKey,
                KeySymbol = released.KeySymbol
            });
        }
    }
}
