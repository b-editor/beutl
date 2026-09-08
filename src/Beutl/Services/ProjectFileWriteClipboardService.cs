using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.ProjectSystem;

namespace Beutl.Services;

internal sealed class ProjectFileWriteAdmission(EditorService editorService)
    : IProjectFileWriteAdmission
{
    IProjectFileWriteLease? IProjectFileWriteAdmission.TryBeginProjectFileWrite()
    {
        return editorService.TryBeginProjectFileWrite();
    }
}

internal sealed class ProjectFileWriteClipboardService(
    EditorService editorService,
    IElementClipboardService inner) : IElementClipboardService
{
    public Task<bool> CopyAsync(IReadOnlyList<Element> elements)
    {
        return inner.CopyAsync(elements);
    }

    public Task<bool> CutAsync(
        Scene scene,
        IReadOnlyList<Element> elements,
        bool ripple = false)
    {
        return inner.CutAsync(scene, elements, ripple);
    }

    public async Task<ElementPasteOutcome> PasteAsync(
        Scene scene,
        TimeSpan clickedFrame,
        int clickedLayer)
    {
        using IProjectFileWriteLease? fileWrite = editorService.TryBeginProjectFileWrite();
        if (fileWrite is null)
        {
            return ElementPasteOutcome.Empty;
        }

        return await inner.PasteAsync(scene, clickedFrame, clickedLayer);
    }
}
