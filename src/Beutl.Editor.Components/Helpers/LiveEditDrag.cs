using Avalonia.Controls;

namespace Beutl.Editor.Components.Helpers;

// A drag that writes as it goes and is confirmed once, as on a gradient slider or a color picker. The writes go to a
// DragSession, so playback start keeps them and Escape and Undo revert them. A deferred drag holds the same session but
// writes only when it is confirmed.
internal sealed class LiveEditDrag
{
    private DragSession? _drag;
    private Action? _stop;
    private Action? _apply;
    private Action? _revert;

    // Runs write in the current session, opening it on owner if needed. A write without a session applies immediately.
    public void Write(IServiceProvider editor, Control? owner, Action stop, Action write)
    {
        Open(editor, owner, stop);
        using (_drag?.Enter())
        {
            write();
        }
    }

    // Opens the drag's session on owner without writing, for an edit that only the control shows until the drag ends:
    // confirming the session runs apply in it, and cancelling it runs revert. Without a session, as when another
    // gesture holds history, the control's own confirmation applies the edit.
    public void Defer(IServiceProvider editor, Control? owner, Action stop, Action apply, Action revert)
    {
        Open(editor, owner, stop);
        if (_drag is not null)
        {
            _apply = apply;
            _revert = revert;
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

    public DragSession? Open(IServiceProvider editor, Control? owner, Action stop)
    {
        if (_drag is null && owner is not null)
        {
            _drag = DragSession.Begin(editor, owner, CommandNames.EditProperty, OnConfirmed, OnCancelled);
            if (_drag is not null)
                _stop = stop;
        }

        return _drag;
    }

    private void OnConfirmed()
    {
        DragSession? drag = _drag;
        Action? apply = _apply;
        End();
        if (apply is null)
            return;

        // The session is still open here, so the write joins its undo entry.
        using (drag?.Enter())
        {
            apply();
        }
    }

    private void OnCancelled()
    {
        Action? revert = _revert;
        End();
        revert?.Invoke();
    }

    private void End()
    {
        _drag = null;
        _apply = null;
        _revert = null;
        Action? stop = _stop;
        _stop = null;
        stop?.Invoke();
    }
}
