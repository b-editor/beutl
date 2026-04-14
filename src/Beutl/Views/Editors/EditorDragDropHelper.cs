using Avalonia.Input;
using Avalonia.Platform.Storage;
using Beutl.Editor.Components.Helpers;

namespace Beutl.Views.Editors;

internal static class EditorDragDropHelper
{
    public static bool TryHandleEditorDrop<TItem>(
        DragEventArgs e,
        DataFormat<string> dataFormat,
        Func<string, bool> tryPasteJson,
        Action<TItem> onTemplateInstance,
        Func<Type, bool> onTypePayload) where TItem : class
    {
        if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } droppedFile
            && string.Equals(Path.GetExtension(droppedFile), ".json", StringComparison.OrdinalIgnoreCase)
            && ObjectTemplateService.Instance.TryLoadFromFile(droppedFile) is { } template
            && template.CreateInstance() is TItem instance)
        {
            onTemplateInstance(instance);
            return true;
        }

        if (e.DataTransfer.TryGetValue(dataFormat) is not { } data)
        {
            return false;
        }

        if (CoreObjectClipboard.IsJsonData(data))
        {
            return tryPasteJson(data);
        }

        if (TypeFormat.ToType(data) is { } type)
        {
            return onTypePayload(type);
        }

        return false;
    }

    public static void HandleEditorDragOver(DragEventArgs e, DataFormat<string> dataFormat)
    {
        if (e.DataTransfer.Contains(dataFormat)
            || e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
            e.Handled = true;
        }
    }
}
