using Beutl.Controls;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Media;
using AM = Avalonia.Media;

namespace Beutl.ViewModels.Editors;

// The gradient-stop edits the brush editor and the gradient stops editor share. list is null when
// the edited value holds no stops.
internal static class GradientStopEdits
{
    public static void Insert(BaseEditorViewModel editor, ICoreList<GradientStop>? list, int index, GradientStop item)
    {
        if (!editor.IsElementEditable) return;
        if (list != null)
        {
            list.Insert(index, item);
            editor.Commit();
        }
    }

    public static void Remove(BaseEditorViewModel editor, ICoreList<GradientStop>? list, int index)
    {
        if (!editor.IsElementEditable) return;
        if (list != null)
        {
            list.RemoveAt(index);
            editor.Commit();
        }
    }

    public static void Confirm(BaseEditorViewModel editor, ICoreList<GradientStop>? list, int oldIndex, int newIndex)
    {
        if (!editor.IsElementEditable) return;
        if (list != null)
        {
            if (oldIndex != newIndex)
                list.Move(oldIndex, newIndex);

            editor.Commit();
        }
    }

    // A drag on a gradient slider: moves the stop without recording it, and refreshes the cached frames.
    public static void Drag(BaseEditorViewModel editor, ICoreList<GradientStop> list, int oldIndex, int newIndex, AM.GradientStop stop)
    {
        if (!Contains(list, oldIndex) || !Contains(list, newIndex)) return;

        GradientStop obj = list[oldIndex];
        obj.Offset.CurrentValue = (float)stop.Offset;
        obj.Color.CurrentValue = stop.Color.ToBtlColor();
        if (newIndex != oldIndex)
            list.Move(oldIndex, newIndex);

        editor.InvalidateFrameCache();
    }

    // The end of the drag: moves the stop back first, so that Confirm records the whole move.
    public static void Release(BaseEditorViewModel editor, ICoreList<GradientStop> list, int oldIndex, int newIndex)
    {
        if (!Contains(list, oldIndex) || !Contains(list, newIndex)) return;

        if (newIndex != oldIndex)
            list.Move(newIndex, oldIndex);
        Confirm(editor, list, oldIndex, newIndex);
    }

    private static bool Contains(ICoreList<GradientStop> list, int index) => index >= 0 && index < list.Count;
}
