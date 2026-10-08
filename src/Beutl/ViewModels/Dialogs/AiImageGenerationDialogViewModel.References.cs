using Avalonia.Platform.Storage;
using Beutl.Api.Services;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Services;
using Beutl.Services.AI;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

internal sealed partial class AiImageGenerationDialogViewModel
{
    // The pictures the user picked, as many of them as this model takes. A
    // model that takes fewer sets the rest aside rather than throwing them
    // away, so going back to one that takes them all asks for the same request
    // again rather than a smaller one.
    private void ShowChosenReferenceImages(int maxReferences)
    {
        string[] wanted = WithinTotalLimit(_chosenReferencePaths.Take(maxReferences));
        if (ReferenceImages.Select(reference => reference.Path).SequenceEqual(
                wanted,
                StringComparer.Ordinal))
        {
            return;
        }

        foreach (AiReferenceImageViewModel shown in ReferenceImages)
            shown.Dispose();
        ReferenceImages.Clear();
        foreach (string path in wanted)
        {
            if (LoadReference(path) is { } reference)
                ReferenceImages.Add(reference);
        }
    }

    private void UpdateReferenceImageState()
    {
        HasReferenceImages.Value = ReferenceImages.Count > 0;
        CanAddReferenceImage.Value = ReferenceImages.Count < MaxReferenceImages.Value;
        ReferenceImageCountText.Value = string.Format(
            CultureInfo.CurrentCulture,
            Strings.AiReferenceImageCount,
            ReferenceImages.Count,
            MaxReferenceImages.Value);
    }

    private void ClearReferenceImagesCore()
    {
        foreach (AiReferenceImageViewModel reference in ReferenceImages)
            reference.Dispose();
        ReferenceImages.Clear();
        _chosenReferencePaths.Clear();
        UpdateReferenceImageState();
    }

    private void RemoveReferenceImage(AiReferenceImageViewModel reference)
    {
        if (!ReferenceImages.Remove(reference))
            return;
        _chosenReferencePaths.Remove(reference.Path);
        reference.Dispose();
        UpdateReferenceImageState();
    }

    // What the pictures of one request may come to together is published by the
    // server and can be lowered. Anything over the new total is dropped here
    // rather than refused once the request has been built — which is only ever
    // reached while no name is outstanding, so this cannot rewrite a request
    // waiting to be collected.
    private void TrimReferenceImagesToLimit()
    {
        // Trim preserved references as well as visible ones. Otherwise switching back to a less
        // restrictive model after the limit shrinks would restore an over-limit set.
        string[] within = WithinTotalLimit(_chosenReferencePaths);
        if (within.Length == _chosenReferencePaths.Count)
            return;

        _chosenReferencePaths.Clear();
        _chosenReferencePaths.AddRange(within);
        ShowChosenReferenceImages(MaxReferenceImages.Value);
        UpdateReferenceImageState();
    }

    // What the pictures may come to together is published by the server. The
    // ones that fit, in the order they were picked.
    private string[] WithinTotalLimit(IEnumerable<string> paths)
    {
        long limit = _selectedRecovery?.Form?.MaxReferenceTotalBytes
            ?? ModelPicker.ImageReferenceLimits.MaxTotalBytes;
        long total = 0;
        var within = new List<string>();
        foreach (string path in paths)
        {
            long size = SizeOf(path);
            if (total + size > limit)
                break;
            within.Add(path);
            total += size;
        }

        return within.ToArray();
    }

    private Task SelectReferenceImageAsync() => SelectReferenceImageAsync(null);

    internal async Task SelectReferenceImageAsync(IReadOnlyList<string>? droppedPaths)
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        IReadOnlyList<string> paths;
        IDisposable? selectedFilesOwnership = null;
        if (droppedPaths is not null)
        {
            paths = droppedPaths;
        }
        else if (ReferenceImagePicker is { } picker)
        {
            paths = await picker(operation.CancellationToken);
        }
        else
        {
            if (AiDialogStorage.MainWindowStorage() is not { } storage)
                return;
            FilePickerOpenOptions options = SharedFilePickerOptions.OpenAiInputImage();
            options.AllowMultiple = true;
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(options);
            selectedFilesOwnership = SharedFilePickerOptions.OwnStorageFiles(files);
            paths = files.Select(file => file.Path.LocalPath).ToArray();
        }
        using IDisposable? fileOwnership = selectedFilesOwnership;
        if (paths.Count == 0)
            return;

        operation.TryPublish(() => AddReferenceImages(paths));
    }

    /// <summary>
    /// Adds pictures in the order given, stopping at what the chosen model
    /// takes. One that cannot be read is reported and skipped rather than
    /// stopping the rest.
    /// </summary>
    internal void AddReferenceImages(IEnumerable<string> paths)
    {
        long total = ReferenceImages.Sum(reference => SizeOf(reference.Path));
        foreach (string path in paths)
        {
            if (ReferenceImages.Count >= MaxReferenceImages.Value)
                break;

            // The server holds every picture raw, again as base64 and again
            // through JSON, so what they come to together is bounded as well as
            // what each one may be. Said here rather than after the whole set
            // has been sent for the server to refuse.
            long size = SizeOf(path);
            if (total + size > ModelPicker.ImageReferenceLimits.MaxTotalBytes)
            {
                Error.Value = Strings.AiFileTooLarge;
                break;
            }

            if (LoadReference(path) is { } reference)
            {
                ReferenceImages.Add(reference);
                _chosenReferencePaths.Add(path);
                total += size;
            }
        }

        UpdateReferenceImageState();
    }

    // Build the upload and its name from the same read. Reading twice could make the dispatched
    // bytes differ from the named bytes and record the result under the wrong request.
    private async Task<(
        AiUploadSource[] References,
        string[] Stamps,
        AiRequestRecoverySource[] Sources)>
        ReadReferencesAsync(
            string[] paths,
            long totalLimit,
            CancellationToken cancellationToken,
            IReadOnlyList<AiRequestRecoverySource>? recoveredSources = null)
    {
        var sources = new AiUploadSource[paths.Length];
        var stamps = new string[paths.Length];
        var recoverySources = new AiRequestRecoverySource[paths.Length];
        // Each image and the combined set have limits. Read only the remaining allowance so an
        // over-limit set is rejected before every image has been copied into memory.
        long remaining = totalLimit;
        for (int index = 0; index < paths.Length; index++)
        {
            AiRequestRecoverySource? recovered = recoveredSources is { Count: > 0 }
                ? recoveredSources.FirstOrDefault(source => source.Role == $"reference-{index.ToString(CultureInfo.InvariantCulture)}")
                : null;
            if (recovered is not null && !recovered.MatchesPath(paths[index]))
            {
                // A user-selected locator may be replaced after recovery. Read
                // the current file and let the fingerprint check decide whether
                // it is still the same request.
                recovered = null;
            }
            string fileName = recovered?.Name ?? Path.GetFileName(paths[index]);
            byte[] bytes = recovered is not null
                ? await ReadRecoveredSourceAsync(recovered, cancellationToken)
                : await AiUploadBytes.ReadWithinAsync(
                    paths[index],
                    Math.Min(remaining, AiRequestLimits.MaxImageUploadBytes),
                    cancellationToken);
            remaining -= bytes.LongLength;
            stamps[index] = AiRequestKey.FileStamp(fileName, bytes);
            sources[index] = AiUploadSource.FromBytes(fileName, bytes);
            recoverySources[index] = recovered ?? FileAiRequestRecoveryStore.CreateExternalSource(
                $"reference-{index.ToString(CultureInfo.InvariantCulture)}",
                paths[index],
                fileName,
                bytes);
        }

        return (sources, stamps, recoverySources);
    }

    private async Task<byte[]> ReadRecoveredSourceAsync(
        AiRequestRecoverySource source,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Path is null && source.DurableFile is null)
            throw new InvalidDataException($"AI recovery source '{source.Role}' is unavailable.");
        return _requestKey.ReadSourceBytes(source);
    }

    private static long SizeOf(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private AiReferenceImageViewModel? LoadReference(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        try
        {
            // The upload is refused server-side once it is too big, so the file is
            // measured here instead of after the account has waited for a round trip.
            BeforeReferenceImageSizeProbe?.Invoke(path);
            if (new FileInfo(path).Length > AiRequestLimits.MaxImageUploadBytes)
            {
                Error.Value = Strings.AiFileTooLarge;
                return null;
            }

            return new AiReferenceImageViewModel(
                path,
                Ref<Bitmap>.Create(AiImageDecodeValidator.LoadValidatedBitmap(
                    path,
                    AiRequestLimits.MaxImageUploadBytes)),
                RemoveReferenceImage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load an AI reference image preview from {Path}", path);
            Error.Value = Strings.AiEditSourcePreviewFailed;
            return null;
        }
    }
}
