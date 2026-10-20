using Avalonia.Controls;
using Beutl.Editor.Components.Helpers;
using Beutl.Media;
using Beutl.ViewModels.Editors;
using AM = Avalonia.Media;

namespace Beutl.Views.Editors;

// A gradient slider's stop drag. Its writes go to a DragSession, so playback start keeps them and Escape and Undo
// revert them; stopDrag makes the slider ignore the rest of a drag the session ended.
internal sealed class GradientStopDrag(Func<Control?> owner, Action stopDrag)
{
    private DragSession? _drag;

    public void Move(BaseEditorViewModel editor, ICoreList<GradientStop> list, int oldIndex, int newIndex, AM.GradientStop stop)
    {
        if (_drag is null && owner() is { } control)
            _drag = DragSession.Begin(editor, control, CommandNames.EditProperty, End, End);

        using (_drag?.Enter())
        {
            GradientStopEdits.Drag(editor, list, oldIndex, newIndex, stop);
        }
    }

    public void Release(BaseEditorViewModel editor, ICoreList<GradientStop> list, int oldIndex, int newIndex)
    {
        if (_drag is { } drag)
            drag.Confirm();
        else
            GradientStopEdits.Release(editor, list, oldIndex, newIndex);
    }

    private void End()
    {
        _drag = null;
        stopDrag();
    }
}
