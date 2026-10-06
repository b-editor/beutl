using System.Diagnostics.CodeAnalysis;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Beutl.Editor.Components.Helpers;
using Beutl.ViewModels.Editors;

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
        if (TryLoadDroppedTemplate(e, out ObjectTemplateItem? template)
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

    // Loads the object template saved in a dropped .json file.
    public static bool TryLoadDroppedTemplate(DragEventArgs e, [NotNullWhen(true)] out ObjectTemplateItem? template)
    {
        if (e.DataTransfer.TryGetFile()?.TryGetLocalPath() is { } droppedFile
            && string.Equals(Path.GetExtension(droppedFile), ".json", StringComparison.OrdinalIgnoreCase)
            && ObjectTemplateService.Instance.TryLoadFromFile(droppedFile) is { } loaded)
        {
            template = loaded;
            return true;
        }

        template = null;
        return false;
    }

    public static bool TryApplyDroppedTemplate(DragEventArgs e, BaseEditorViewModel viewModel)
    {
        return TryLoadDroppedTemplate(e, out ObjectTemplateItem? template)
            && viewModel.ApplyTemplate(template);
    }

    public static void HandleTemplateFileDragOver(DragEventArgs e)
    {
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            e.DragEffects = DragDropEffects.Copy | DragDropEffects.Link;
            e.Handled = true;
        }
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
