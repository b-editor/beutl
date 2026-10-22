using Beutl.Controls;
using Beutl.Editor.PropertyEditors;
using Beutl.Media;
using AM = Avalonia.Media;

namespace Beutl.Editor.Components.PropertyEditors.ViewModels;

// The gradient-stop edits the brush editor and the gradient stops editor share. list is null when
// the edited value holds no stops.
internal static class GradientStopEdits
{
    public static void Insert(PropertyEditorViewModel editor, ICoreList<GradientStop>? list, int index, GradientStop item)
    {
        if (!editor.IsElementEditable) return;
        if (list != null)
        {
            list.Insert(index, item);
            editor.Commit();
        }
    }

    public static void Remove(PropertyEditorViewModel editor, ICoreList<GradientStop>? list, int index)
    {
        if (!editor.IsElementEditable) return;
        if (list != null)
        {
            list.RemoveAt(index);
            editor.Commit();
        }
    }

    public static void Confirm(PropertyEditorViewModel editor, ICoreList<GradientStop>? list, int oldIndex, int newIndex)
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
    public static void Drag(PropertyEditorViewModel editor, ICoreList<GradientStop> list, int oldIndex, int newIndex, AM.GradientStop stop)
    {
        if (!Contains(list, oldIndex) || !Contains(list, newIndex)) return;

        GradientStop obj = list[oldIndex];
        obj.Offset.Value = (float)stop.Offset;
        obj.Color.Value = stop.Color.ToBtlColor();
        if (newIndex != oldIndex)
            list.Move(oldIndex, newIndex);

        editor.InvalidateFrameCache();
    }

    // The end of the drag: moves the stop back first, so that Confirm records the whole move.
    public static void Release(PropertyEditorViewModel editor, ICoreList<GradientStop> list, int oldIndex, int newIndex)
    {
        if (!Contains(list, oldIndex) || !Contains(list, newIndex)) return;

        if (newIndex != oldIndex)
            list.Move(newIndex, oldIndex);
        Confirm(editor, list, oldIndex, newIndex);
    }

    private static bool Contains(ICoreList<GradientStop> list, int index) => index >= 0 && index < list.Count;
}
