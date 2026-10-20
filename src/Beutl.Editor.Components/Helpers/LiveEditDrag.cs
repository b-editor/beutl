using Avalonia.Controls;

namespace Beutl.Editor.Components.Helpers;

// A drag that writes as it goes and is confirmed once, as on a gradient slider or a color picker. The writes go to a
// DragSession, so playback start keeps them and Escape and Undo revert them.
internal sealed class LiveEditDrag
{
    private DragSession? _drag;
    private Action? _stop;

    // Runs write in the drag's session, which the first write opens on owner; stop ends the control's drag in progress.
    // Without an owner, as for typed input, write runs outside any session.
    public void Write(IServiceProvider editor, Control? owner, Action stop, Action write)
    {
        if (_drag is null && owner is not null)
        {
            _drag = DragSession.Begin(editor, owner, CommandNames.EditProperty, End, End);
            if (_drag is not null)
                _stop = stop;
        }

        using (_drag?.Enter())
        {
            write();
        }
    }

    // False when no session holds the drag, so the caller commits as it did without one.
    public bool Confirm()
    {
        if (_drag is not { } drag)
            return false;

        drag.Confirm();
        return true;
    }

    private void End()
    {
        _drag = null;
        Action? stop = _stop;
        _stop = null;
        stop?.Invoke();
    }
}
