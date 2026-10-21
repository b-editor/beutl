using Avalonia.Platform.Storage;
using Beutl.Api.Services;
using Beutl.Editor.Components.AiWorkspaceTab.Services;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services.AI;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.Editor.Components.AiWorkspaceTab.ViewModels;

internal sealed partial class AiVideoGenerationDialogViewModel
{
    internal async Task SelectFrameAsync(bool isFirstFrame, string? droppedPath = null)
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        string? path;
        IDisposable? selectedFilesOwnership = null;
        if (droppedPath is not null)
        {
            path = droppedPath;
        }
        else if (FramePicker is { } picker)
        {
            path = await picker(operation.CancellationToken);
        }
        else
        {
            if (AiDialogStorage.MainWindowStorage() is not { } storage)
                return;
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(
                SharedFilePickerOptions.OpenAiVideoFrame());
            selectedFilesOwnership = SharedFilePickerOptions.OwnStorageFiles(files);
            path = files.Count > 0 ? files[0].Path.LocalPath : null;
        }
        using IDisposable? fileOwnership = selectedFilesOwnership;
        if (path is not null)
        {
            operation.TryPublish(() => SetFrameCore(isFirstFrame, path));
        }
    }

    private async Task CaptureCurrentFrameAsync()
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        if (_editor is null)
            return;

        CancellationToken lifetimeToken = operation.CancellationToken;
        if (!operation.TryPublish(() => Error.Value = null))
            return;

        string? unpublishedPath = null;
        try
        {
            using Bitmap bitmap = CurrentFrameRenderer is { } renderer
                ? await renderer(lifetimeToken)
                : await _editor.GetRequiredService<IFullScaleFrameRenderer>().DrawFrameAtFullScale();
            lifetimeToken.ThrowIfCancellationRequested();

            (unpublishedPath, FileStream stream) = AiTemporaryFileStore.Create(
                "inputs",
                "frame",
                ".png");
            using (stream)
            {
                bitmap.Save(stream, EncodedImageFormat.Png);
            }
            lifetimeToken.ThrowIfCancellationRequested();

            bool accepted = false;
            bool published = operation.TryPublish(() =>
            {
                lock (_lifetimeGate)
                {
                    _temporaryFiles.Add(unpublishedPath);
                    try
                    {
                        accepted = SetFrameCore(isFirstFrame: true, unpublishedPath);
                        if (!accepted) _temporaryFiles.Remove(unpublishedPath);
                    }
                    catch
                    {
                        _temporaryFiles.Remove(unpublishedPath);
                        throw;
                    }
                }
            });
            if (!published || !accepted)
                return;

            unpublishedPath = null;
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture the current frame for AI video generation.");
            operation.TryPublish(() => Error.Value = Strings.AiVideoFrameCaptureFailed);
        }
        finally
        {
            if (unpublishedPath is not null)
            {
                DeleteTemporaryFile(unpublishedPath);
            }
        }
    }

    private void SetFrame(bool isFirstFrame, string? path)
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        operation?.TryPublish(() => SetFrameCore(isFirstFrame, path));
    }

    private bool SetFrameCore(bool isFirstFrame, string? path)
    {
        if (!string.IsNullOrEmpty(path)
            && File.Exists(path)
            && new FileInfo(path).Length > AiRequestLimits.MaxFrameUploadBytes)
        {
            Error.Value = Strings.AiFileTooLarge;
            return false;
        }

        // Decode before replacing the selection or releasing its temporary frame.
        Ref<Bitmap>? preview = null;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                preview = Ref<Bitmap>.Create(
                    AiImageDecodeValidator.LoadValidatedBitmap(path, AiRequestLimits.MaxFrameUploadBytes));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load an AI video frame preview from {Path}", path);
                Error.Value = Strings.AiEditSourcePreviewFailed;
                return false;
            }
        }

        // Preserve a user-selected frame when model constraints merely hide it, so restoring the
        // old model also restores the same request with the same frame.
        if (!_applyingCapabilities)
        {
            if (isFirstFrame)
                _chosenFirstFrame = path;
            else
                _chosenLastFrame = path;
        }

        ReactivePropertySlim<string?> pathProperty = isFirstFrame ? FirstFramePath : LastFramePath;
        ReactivePropertySlim<Ref<Bitmap>?> previewProperty = isFirstFrame ? FirstFramePreview : LastFramePreview;
        string? previousPath = pathProperty.Value;
        previewProperty.Value?.Dispose();
        previewProperty.Value = null;
        pathProperty.Value = path;

        // Do not delete a temporary frame that model constraints only hid; it is required to
        // reconstruct the same request after restoring the old model.
        if (!_applyingCapabilities
            && !string.Equals(previousPath, path, StringComparison.Ordinal))
        {
            RequestTemporaryFileDeletion(previousPath);
        }

        previewProperty.Value = preview;
        return true;
    }

    // Build the upload and its name from the same read. Reading twice could make the dispatched
    // bytes differ from the named bytes and record the result under the wrong request.
    private async Task<(AiUploadSource? Frame, string Stamp)> ReadFrameAsync(
        string? path,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(path))
            return (null, string.Empty);

        byte[] bytes = await AiUploadBytes.ReadWithinAsync(
            path,
            AiRequestLimits.MaxFrameUploadBytes,
            cancellationToken);
        // Exclude the filename: the server identifies a frame by its content and type. Scene
        // captures get a new temporary filename each time, which must not turn a resend of the
        // same frame into a newly charged request.
        return (AiUploadSource.FromBytes(Path.GetFileName(path), bytes), AiRequestKey.ContentStamp(bytes));
    }
}
