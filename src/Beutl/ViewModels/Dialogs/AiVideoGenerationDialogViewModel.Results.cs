using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

internal sealed partial class AiVideoGenerationDialogViewModel
{
    private void OpenResultCore()
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        if (ResultVideoPath.Value is not { } path || !File.Exists(path))
            return;

        try
        {
            operation.TryPublish(() =>
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "open" }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open the generated AI video preview.");
            operation.TryPublish(() => Error.Value = Strings.AiVideoPreviewOpenFailed);
        }
    }

    private async Task AddToSceneCore()
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        if (_editViewModel == null || ResultVideoPath.Value is not { } filePath)
            return;
        if (!operation.IsCurrent)
            return;
        using IDisposable fileLease = AcquireTemporaryFileLease(filePath);

        try
        {
            double durationSeconds = _resultSnapshot?.DurationSeconds ?? SelectedDuration.Value.Seconds;
            AiResultImportOptions options = AiDialogResults.PlaceAtPlayhead(
                _editViewModel,
                TimeSpan.FromSeconds(durationSeconds),
                Strings.AiVideoGeneration);
            ElementAddResult result;
            if (ResultImporter is { } importer)
            {
                result = await importer(filePath, options, operation.CancellationToken);
            }
            else
            {
                var defaultImporter = new AiResultImporter(
                    _editViewModel.Scene,
                    _editViewModel.GetRequiredService<IElementAdder>());
                result = await defaultImporter.ImportVideoAsync(
                    filePath,
                    options,
                    operation.CancellationToken);
            }

            AiDialogResults.PublishImport(
                operation,
                result,
                Strings.AiVideoGeneration,
                Strings.AiVideoAddedToScene,
                "generated video");
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add the AI video to the scene.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
        }

    }

    private async Task SaveToFileCore()
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        if (ResultVideoPath.Value is not { } filePath)
            return;
        using IDisposable fileLease = AcquireTemporaryFileLease(filePath);

        (AiSaveFileDestination? destination, IStorageFile? selectedStorageFile) =
            await AiDialogStorage.PickSaveDestinationAsync(
                SaveFilePicker,
                () =>
                {
                    FilePickerSaveOptions options = SharedFilePickerOptions.SaveVideo();
                    options.SuggestedFileName = $"AI Video {DateTime.Now:yyyy-MM-dd HHmmss}";
                    options.DefaultExtension = Path.GetExtension(filePath).TrimStart('.');
                    return options;
                },
                WellKnownFolder.Videos,
                operation.CancellationToken);
        using IStorageFile? storageFileOwnership = selectedStorageFile;

        if (destination is null || !operation.IsCurrent)
            return;

        try
        {
            string sourceExtension = Path.GetExtension(filePath);
            string destinationExtension = Path.GetExtension(destination.Path);
            if (!IsSupportedVideoExtension(destinationExtension)
                || !string.Equals(sourceExtension, destinationExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The destination must use the generated video's format.");
            }

            string temporaryPath = destination.Path + $".{Guid.NewGuid():N}.tmp";
            try
            {
                UnixFileMode? existingMode = !OperatingSystem.IsWindows() && File.Exists(destination.Path)
                    ? File.GetUnixFileMode(destination.Path)
                    : null;
                await CopyVideoFileAsync(filePath, temporaryPath, operation.CancellationToken);
                if (!OperatingSystem.IsWindows() && existingMode is { } mode)
                    File.SetUnixFileMode(temporaryPath, mode);
                if (!operation.IsCurrent)
                    return;

                // The only UI publication is the same-volume rename. The old destination
                // remains untouched if the copy or cancellation fails beforehand.
                if (!operation.TryPublish(() => File.Move(temporaryPath, destination.Path, true)))
                    return;
            }
            finally
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
            operation.TryPublish(() =>
                NotificationService.ShowSuccess(Strings.AiVideoGeneration, Strings.AiVideoSaved));
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save the AI video.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
        }
    }

    private static async Task CopyVideoFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = 128 * 1024,
                Options = FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough,
            });
        await source.CopyToAsync(destination, 128 * 1024, cancellationToken);
        await destination.FlushAsync(cancellationToken);
        destination.Flush(true);
    }

    private static bool IsSupportedVideoExtension(string extension)
        => extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webm", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase);
}
