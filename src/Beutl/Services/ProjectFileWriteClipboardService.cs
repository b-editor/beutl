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

    ValueTask<IProjectFileWriteLease> IProjectFileWriteAdmission.BeginProjectFileWriteAsync(
        CancellationToken cancellationToken)
    {
        return editorService.BeginProjectFileWriteAsync(cancellationToken);
    }
}

internal sealed class ProjectFileWriteClipboardService(
    EditorService editorService,
    IElementClipboardService inner,
    CancellationToken lifetime = default) : IElementClipboardService
{
    public Task<bool> CopyAsync(IReadOnlyList<Element> elements)
    {
        return inner.CopyAsync(elements);
    }

    public async Task<bool> CutAsync(
        Scene scene,
        IReadOnlyList<Element> elements,
        bool ripple = false)
    {
        using IProjectFileWriteLease? fileWrite = await BeginWriteAsync();
        if (fileWrite is null)
        {
            return false;
        }

        return await inner.CutAsync(scene, elements, ripple);
    }

    public async Task<ElementPasteOutcome> PasteAsync(
        Scene scene,
        MediaTime clickedFrame,
        int clickedLayer)
    {
        using IProjectFileWriteLease? fileWrite = await BeginWriteAsync();
        if (fileWrite is null)
        {
            return ElementPasteOutcome.Empty;
        }

        return await inner.PasteAsync(scene, clickedFrame, clickedLayer);
    }

    private async ValueTask<IProjectFileWriteLease?> BeginWriteAsync()
    {
        try { return await editorService.TryBeginProjectFileWriteAsync(lifetime); }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return null; }
    }
}
