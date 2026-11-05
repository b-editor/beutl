using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Beutl.Editor.Components.Helpers;

// A Space key that pans while held and, when tapped, still does what the key map binds to it.
// Only its release can tell the two apart, so the owner keeps the press from the key map until then.
internal sealed class SpacePanKey
{
    private readonly Control _owner;
    // What the held key was pressed with, until a pointer press makes the hold a gesture rather than a tap.
    private KeyModifiers? _tap;

    public SpacePanKey(Control owner)
    {
        _owner = owner;
        owner.AddHandler(InputElement.PointerPressedEvent, (_, _) => _tap = null, RoutingStrategies.Tunnel);
    }

    public bool IsPressed { get; private set; }

    // canTap is false during a pointer gesture. A held key repeats its press, which changes nothing.
    public void Press(KeyEventArgs e, bool canTap)
    {
        if (!IsPressed && canTap) _tap = e.KeyModifiers;
        IsPressed = true;
        e.Handled = true;
    }

    public void Release(KeyEventArgs e)
    {
        KeyModifiers? tap = _tap;
        Reset();
        if (tap is { } modifiers) ReplayTap(e, modifiers);
    }

    public void Reset()
    {
        IsPressed = false;
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
