using Avalonia.Platform.Storage;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Beutl.Editor.Services.AI;
using Beutl.Media;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Reactive.Bindings;

namespace Beutl.Editor.Components.AiWorkspaceTab.ViewModels;

/// <summary>
/// Where an AI dialog puts its result in the scene or on disk, and what the person is told about it.
/// </summary>
internal static class AiDialogResults
{
    // At the playhead, on the first layer that is free there.
    public static AiResultImportOptions PlaceAtPlayhead(IAiJobResultEditorContext editor, TimeSpan length, string name)
    {
        TimeSpan start = editor.CurrentTime;
        return new AiResultImportOptions(start, length, editor.GetNextLayer(start), name);
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

    public static Task AddImageToSceneAsync(
        IdentityOperationLifetime.Operation operation,
        IAiJobResultEditorContext editor,
        Bitmap bitmap,
        Func<Bitmap, AiResultImportOptions, CancellationToken, Task<ElementAddResult>>? importer,
        string title,
        string resultNoun,
        ReactivePropertySlim<string?> error,
        Action<Exception> reportFailure)
        => AddToSceneAsync(
            operation,
            editor,
            5,
            (options, token) => importer is not null
                ? importer(bitmap, options, token)
                : DefaultImporter(editor).ImportImageAsync(bitmap, options, token),
            title,
            Strings.AiImageAddedToScene,
            resultNoun,
            error,
            reportFailure);

    public static Task AddVideoToSceneAsync(
        IdentityOperationLifetime.Operation operation,
        IAiJobResultEditorContext editor,
        string filePath,
        double seconds,
        Func<string, AiResultImportOptions, CancellationToken, Task<ElementAddResult>>? importer,
        ReactivePropertySlim<string?> error,
        Action<Exception> reportFailure)
        => AddToSceneAsync(
            operation,
            editor,
            seconds,
            (options, token) => importer is not null
                ? importer(filePath, options, token)
                : DefaultImporter(editor).ImportVideoAsync(filePath, options, token),
            Strings.AiVideoGeneration,
            Strings.AiVideoAddedToScene,
            "generated video",
            error,
            reportFailure);

    // Adds what import makes at the playhead, for seconds. A failure is handed to reportFailure
    // and shown in error as an unexpected one.
    private static async Task AddToSceneAsync(
        IdentityOperationLifetime.Operation operation,
        IAiJobResultEditorContext editor,
        double seconds,
        Func<AiResultImportOptions, CancellationToken, Task<ElementAddResult>> import,
        string title,
        string addedMessage,
        string resultNoun,
        ReactivePropertySlim<string?> error,
        Action<Exception> reportFailure)
    {
        try
        {
            AiResultImportOptions options = PlaceAtPlayhead(editor, TimeSpan.FromSeconds(seconds), title);
            ElementAddResult result = await import(options, operation.CancellationToken);
            PublishImport(operation, result, title, addedMessage, resultNoun);
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            reportFailure(ex);
            operation.TryPublish(() => error.Value = Strings.AiUnexpectedError);
        }
    }

    private static AiResultImporter DefaultImporter(IAiJobResultEditorContext editor)
        => new(editor.Scene, editor.GetRequiredService<IElementAdder>());

    // Saves bitmap as a PNG where the person picks, suggesting suggestedName and the time. A
    // failure is handed to reportFailure and shown in error as an unexpected one.
    public static async Task SavePngAsync(
        IdentityOperationLifetime.Operation operation,
        Bitmap bitmap,
        Func<CancellationToken, Task<AiSaveFileDestination?>>? picker,
        string suggestedName,
        string encodeFailure,
        string title,
        ReactivePropertySlim<string?> error,
        Action<Exception> reportFailure)
    {
        (AiSaveFileDestination? destination, IStorageFile? selectedStorageFile) =
            await AiDialogStorage.PickSaveDestinationAsync(
                picker,
                () =>
                {
                    FilePickerSaveOptions options = SharedFilePickerOptions.SavePngImage();
                    options.SuggestedFileName = $"{suggestedName} {DateTime.Now:yyyy-MM-dd HHmmss}";
                    options.DefaultExtension = "png";
                    return options;
                },
                WellKnownFolder.Pictures,
                operation.CancellationToken);
        using IStorageFile? storageFileOwnership = selectedStorageFile;

        if (destination is null || !operation.IsCurrent)
            return;

        try
        {
            AiImageFileFormat.ValidatePngDestination(destination.Path);

            if (!operation.TryPublish(() =>
                {
                    operation.CancellationToken.ThrowIfCancellationRequested();
                    AiAtomicFileWriter.WritePng(
                        destination.Path,
                        bitmap,
                        encodeFailure,
                        operation.CancellationToken);
                }))
                return;
            operation.TryPublish(() =>
                NotificationService.ShowSuccess(title, Strings.AiImageSaved));
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            reportFailure(ex);
            operation.TryPublish(() => error.Value = Strings.AiUnexpectedError);
        }
    }
}
