using Avalonia.Platform.Storage;
using Beutl.Api.Services;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Services;
using Beutl.Services.AI;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

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
            operation.TryPublish(() =>
                SetFrameCore(isFirstFrame, path, sourceElementId: null));
        }
    }

    private async Task CaptureCurrentFrameAsync()
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        if (_editViewModel is null)
            return;

        CancellationToken lifetimeToken = operation.CancellationToken;
        if (!operation.TryPublish(() => Error.Value = null))
            return;

        string? unpublishedPath = null;
        try
        {
            using Bitmap bitmap = CurrentFrameRenderer is { } renderer
                ? await renderer(lifetimeToken)
                : await _editViewModel.Player.DrawFrameAtFullScale();
            lifetimeToken.ThrowIfCancellationRequested();

            lifetimeToken.ThrowIfCancellationRequested();
            (unpublishedPath, FileStream stream) = AiTemporaryFileStore.Create(
                "inputs",
                "frame",
                ".png");
            using (stream)
            {
                bitmap.Save(stream, EncodedImageFormat.Png);
            }
            if (new FileInfo(unpublishedPath).Length > AiRequestLimits.MaxFrameUploadBytes)
                throw new AiFileTooLargeException();
            lifetimeToken.ThrowIfCancellationRequested();

            bool accepted = false;
            bool published = operation.TryPublish(() =>
            {
                lock (_lifetimeGate)
                {
                    _temporaryFiles.Add(unpublishedPath);
                    try
                    {
                        accepted = SetFrameCore(isFirstFrame: true, unpublishedPath, sourceElementId: null);
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
        catch (AiFileTooLargeException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiFileTooLarge);
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

    private void SetFrame(bool isFirstFrame, string? path, string? sourceElementId = null)
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        operation?.TryPublish(() => SetFrameCore(isFirstFrame, path, sourceElementId));
    }

    private bool SetFrameCore(bool isFirstFrame, string? path, string? sourceElementId)
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
            {
                _chosenFirstFrame = (path, sourceElementId);
            }
            else
            {
                _chosenLastFrame = (path, sourceElementId);
            }
        }

        ReactivePropertySlim<string?> pathProperty = isFirstFrame ? FirstFramePath : LastFramePath;
        ReactivePropertySlim<Ref<Bitmap>?> previewProperty = isFirstFrame ? FirstFramePreview : LastFramePreview;
        string? previousPath = pathProperty.Value;
        previewProperty.Value?.Dispose();
        previewProperty.Value = null;
        pathProperty.Value = path;
        if (isFirstFrame)
        {
            _firstFrameElementId = sourceElementId;
        }
        else
        {
            _lastFrameElementId = sourceElementId;
        }

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
    private async Task<(
        AiUploadSource? Frame,
        string Stamp,
        byte[]? Bytes,
        string? Name)> ReadFrameAsync(
        string? path,
        CancellationToken cancellationToken,
        AiRequestRecoverySource? recoveredSource = null)
    {
        if (string.IsNullOrEmpty(path))
            return (null, string.Empty, null, null);

        string fileName = recoveredSource?.Name ?? Path.GetFileName(path);
        byte[] bytes = recoveredSource is not null
            ? _requestKey.ReadSourceBytes(recoveredSource)
            : await AiUploadBytes.ReadWithinAsync(
                path,
                AiRequestLimits.MaxFrameUploadBytes,
                cancellationToken);
        // Exclude the filename: the server identifies a frame by its content and type. Scene
        // captures get a new temporary filename each time, which must not turn a resend of the
        // same frame into a newly charged request.
        return (
            AiUploadSource.FromBytes(fileName, bytes),
            AiRequestKey.ContentStamp(bytes),
            bytes,
            fileName);
    }

    // The frame the recovered request was named with, as long as the dialog still shows it.
    private AiRequestRecoverySource? FindRecoveredFrameSource(string role, string? path)
    {
        AiRequestRecoverySource? recovered = _selectedRecovery?.EffectiveSources
            .FirstOrDefault(source => source.Role == role);
        if (recovered is not null && !recovered.MatchesPath(path))
            recovered = null;
        return recovered;
    }

    // A captured frame lives in a temporary file, so it is kept as a durable copy when the
    // key can hold one; a frame the person chose is pointed at where it is.
    private AiRequestRecoverySource CreateFrameRecoverySource(
        string role,
        string path,
        string? name,
        byte[] bytes,
        string? elementId)
        => IsTemporaryFile(path) && _requestKey.HasDurableRecovery
            ? _requestKey.CreateDurableSource(
                role,
                name ?? Path.GetFileName(path),
                bytes,
                elementId)
            : FileAiRequestRecoveryStore.CreateExternalSource(
                role,
                path,
                name ?? Path.GetFileName(path),
                bytes,
                elementId);
}
