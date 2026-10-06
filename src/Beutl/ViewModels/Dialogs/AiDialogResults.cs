using Beutl.Editor.Services.AI;
using Beutl.Services;
using Beutl.Services.AI;

namespace Beutl.ViewModels.Dialogs;

/// <summary>
/// Where an AI dialog puts its result in the scene, and what the person is told about it.
/// </summary>
internal static class AiDialogResults
{
    // At the playhead, on the first layer that is free there.
    public static AiResultImportOptions PlaceAtPlayhead(EditViewModel editor, TimeSpan length, string name)
    {
        IAiJobResultEditorContext context = editor;
        TimeSpan start = context.CurrentTime;
        return new AiResultImportOptions(start, length, context.GetNextLayer(start), name);
    }

    // A locked layer is the person's choice and only warned about; any other failure throws so the
    // caller logs it and reports an unexpected error.
    public static void PublishImport(
        IdentityOperationLifetime.Operation operation,
        ElementAddResult result,
        string title,
        string addedMessage,
        string resultNoun)
    {
        if (result.Failure is LockedElementLayerFailure)
        {
            operation.TryPublish(() =>
                NotificationService.ShowWarning(Strings.Lock, Strings.LayerIsLocked));
            return;
        }

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to add the {resultNoun}: {result.Failure?.Id}.",
                result.Failure?.Exception);
        }

        operation.TryPublish(() => NotificationService.ShowSuccess(title, addedMessage));
    }
}
