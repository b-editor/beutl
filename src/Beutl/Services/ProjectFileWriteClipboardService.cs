using Beutl.Editor.Services;
using Beutl.Editor.VersionControl;
using Beutl.Language;
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
        using IProjectFileWriteLease? fileWrite = await BeginWriteAsync(Strings.Cut);
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
        using IProjectFileWriteLease? fileWrite = await BeginWriteAsync(Strings.Paste);
        if (fileWrite is null)
        {
            return ElementPasteOutcome.Empty;
        }

        return await inner.PasteAsync(scene, clickedFrame, clickedLayer);
    }

    private async ValueTask<IProjectFileWriteLease?> BeginWriteAsync(string title)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        using var dismissal = new CancellationTokenSource();
        try
        {
            ValueTask<IProjectFileWriteLease?> pending = editorService.TryBeginProjectFileWriteAsync(cancellation.Token);
            if (!pending.IsCompleted)
            {
                Task<IProjectFileWriteLease?> wait = pending.AsTask();
                await Task.WhenAny(wait, Task.Delay(TimeSpan.FromMilliseconds(500), dismissal.Token));
                if (!wait.IsCompleted)
                {
                    NotificationService.Show(new Notification(title, Strings.Clipboard_WaitingForSave,
                        Expiration: Timeout.InfiniteTimeSpan,
                        Actions: [new NotificationAction(Strings.Cancel, cancellation.Cancel)], IsClosable: false)
                    {
                        CancellationToken = dismissal.Token
                    });
                }
                return await wait;
            }
            return await pending;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return null; }
        finally { dismissal.Cancel(); }
    }
}
