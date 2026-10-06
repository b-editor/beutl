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
            TimeSpan start = _editViewModel.Player.CurrentFrame.Value;
            int layer = _editViewModel.Scene.Children
                .Where(item => item.Start <= start && start < item.Range.End)
                .Select(item => item.ZIndex)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            double durationSeconds = _resultSnapshot?.DurationSeconds ?? SelectedDuration.Value.Seconds;
            AiResultImportOptions options = new(
                start,
                TimeSpan.FromSeconds(durationSeconds),
                layer,
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

            if (result.Failure is LockedElementLayerFailure)
            {
                operation.TryPublish(() =>
                    NotificationService.ShowWarning(Strings.Lock, Strings.LayerIsLocked));
                return;
            }
            EnsureImportSucceeded(result);
            if (result.IsSuccess)
            {
                operation.TryPublish(() =>
                    NotificationService.ShowSuccess(Strings.AiVideoGeneration, Strings.AiVideoAddedToScene));
            }
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

    private static void EnsureImportSucceeded(ElementAddResult result)
    {
        if (result.IsSuccess)
            return;
        throw new InvalidOperationException(
            $"Failed to add the generated video: {result.Failure?.Id}.",
            result.Failure?.Exception);
    }

    private async Task SaveToFileCore()
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        if (ResultVideoPath.Value is not { } filePath)
            return;
        using IDisposable fileLease = AcquireTemporaryFileLease(filePath);

        AiSaveFileDestination? destination;
        IStorageFile? selectedStorageFile = null;
        if (SaveFilePicker is { } picker)
        {
            destination = await picker(operation.CancellationToken);
        }
        else
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
                { MainWindow: { } window }
                || TopLevel.GetTopLevel(window)?.StorageProvider is not { } storage)
                return;
            FilePickerSaveOptions options = SharedFilePickerOptions.SaveVideo();
            options.SuggestedFileName = $"AI Video {DateTime.Now:yyyy-MM-dd HHmmss}";
            options.SuggestedStartLocation = await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Videos);
            options.DefaultExtension = Path.GetExtension(filePath).TrimStart('.');
            selectedStorageFile = await storage.SaveFilePickerAsync(options);
            destination = selectedStorageFile is null
                ? null
                : new AiSaveFileDestination(selectedStorageFile.Path.LocalPath);
        }
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
