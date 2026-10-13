using Beutl.Media;

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
}
