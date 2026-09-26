using System.Globalization;
using Beutl.Api.Services;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using Microsoft.Extensions.Logging;

namespace Beutl.Services.AI;

/// <summary>
/// Runs node-graph generations through the same safeguards as the AI dialogs:
/// idempotency keys, the balance check, withdrawal of unreserved names and the
/// validated download. Results are saved next to the scene, like dialog imports.
/// </summary>
internal sealed class AiGenerativeNodeExecutor(
    Scene scene,
    AiGenerativeModelCatalog models,
    IAiImageGenerationService images,
    IAiOperationAvailabilityService availability,
    IAuthenticatedContentService content,
    IGenerativePromptLibrary? promptLibrary = null,
    IAiImageEditingService? editing = null) : IGenerativeNodeExecutor
{
    private static readonly ILogger s_logger = Log.CreateLogger<AiGenerativeNodeExecutor>();

    public Task<GenerativeExecutionResult> ExecuteAsync(
        GenerativeRequest request,
        IProgress<GenerativeProgress> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request switch
        {
            AiImageGenerationNodeRequest image => GenerateImageAsync(image, progress, cancellationToken),
            AiImageEditNodeRequest edit => EditImageAsync(edit, progress, cancellationToken),
            _ => throw new NotSupportedException($"{request.Operation} is not supported."),
        };
    }

    private async Task<GenerativeExecutionResult> GenerateImageAsync(
        AiImageGenerationNodeRequest request,
        IProgress<GenerativeProgress> progress,
        CancellationToken cancellationToken)
    {
        // Already written out by a prompt node (or typed directly); only the limit is checked.
        string prompt = request.Prompt;
        if (prompt.Length > AiRequestLimits.MaxPromptLength)
            throw new GenerativeExecutionException(AiPromptComposer.PromptTooLongMessage);
        // Checked against the catalog before anything is reserved, as the dialog's controls
        // are: a request the model would refuse is refused here, for free.
        (AiModelCatalog catalog, IReadOnlyList<GenerativeModelInfo> offered) =
            await models.LoadAsync(request.CatalogOperationId, cancellationToken);
        GenerativeModelInfo? chosen = ResolveModel(request.ModelId, offered);
        GenerativeImageCapabilities? capabilities = chosen?.Image;
        if (capabilities is not null)
        {
            if (!capabilities.AspectRatioChoices.Contains(request.AspectRatio, StringComparer.Ordinal)
                || !capabilities.BackgroundChoices.Contains(request.Background, StringComparer.Ordinal)
                || request.References.Count > capabilities.MaxReferenceImages)
            {
                throw new GenerativeExecutionException(Strings.AiModelDoesNotSupportRequest);
            }
        }

        int? seed = capabilities is { SupportsSeed: false } ? null : request.Seed;
        AiModelId? model = chosen is not null ? new AiModelId(chosen.Id) : null;
        AiImageReferenceLimits referenceLimits = catalog.GetImageReferenceLimits(AiOperations.ImageGeneration);
        AiUploadSource[] references = request.References
            .Select(reference => AiUploadSource.FromBytes(reference.Name, reference.EncodedPng))
            .ToArray();
        // Laid out as the dialog lays out its key, so the server sees the same kind of request.
        string?[] parts =
        [
            prompt,
            request.AspectRatio,
            request.Background,
            seed?.ToString(CultureInfo.InvariantCulture),
            model?.Value,
            .. request.References.Select(reference =>
                AiRequestKey.FileStamp(reference.Name, reference.EncodedPng)),
        ];

        string path = await RunImageAsync(
            request.RequestKeySeed,
            "image.generate",
            parts,
            token => availability.CheckAsync(
                new AiOperationAvailabilityRequest.Fixed(AiOperations.ImageGeneration, model),
                token),
            (key, token) => images.GenerateAsync(
                new AiImageGenerationRequest(
                    prompt,
                    new AiImageAspectRatioId(request.AspectRatio),
                    new AiImageBackgroundId(request.Background),
                    seed: seed,
                    references: references,
                    model: model,
                    idempotencyKey: key,
                    referenceLimits: referenceLimits),
                new Progress<AiImagePreview>(preview => ReportPreview(preview, progress)),
                token),
            progress,
            cancellationToken);
        // As the dialog does once a picture is in hand.
        promptLibrary?.Record(request.Operation, prompt);
        return new GenerativeExecutionResult(new Uri(path), model?.Value, seed);
    }

    private async Task<GenerativeExecutionResult> EditImageAsync(
        AiImageEditNodeRequest request,
        IProgress<GenerativeProgress> progress,
        CancellationToken cancellationToken)
    {
        if (editing is null)
            throw new GenerativeExecutionException(Beutl.Language.NodeGraphStrings.Generative_ExecutorUnavailable);

        string task = request.Task.ToId();
        (_, IReadOnlyList<GenerativeModelInfo> offered) =
            await models.LoadAsync(request.CatalogOperationId, cancellationToken);
        GenerativeModelInfo? chosen = ResolveModel(request.ModelId, offered);
        AiModelId? model = chosen is not null ? new AiModelId(chosen.Id) : null;

        // Prepared as the AI tab prepares it: an outpaint sends the canvas already widened,
        // under a name derived from the source, with the instruction in front of the prompt.
        string uploadName = request.Image.Name;
        byte[] uploadBytes = request.Image.EncodedPng;
        string? prompt = request.Prompt;
        if (request.Task == AiImageEditTask.Outpaint)
        {
            uploadBytes = ExpandCanvas(uploadBytes, request.OutpaintExpansionPercent ?? 25);
            uploadName = $"{Path.GetFileNameWithoutExtension(uploadName)}-outpaint.png";
            prompt = $"Extend the image naturally into the transparent canvas while preserving the original center. {prompt}";
        }

        if (uploadBytes.Length > AiRequestLimits.MaxImageUploadBytes)
            throw new GenerativeExecutionException(Strings.AiFileTooLarge);
        if (prompt is not null && prompt.Length > AiRequestLimits.MaxPromptLength)
            throw new GenerativeExecutionException(AiPromptComposer.PromptTooLongMessage);

        // Laid out as the dialog lays out its key.
        string?[] parts = [task, prompt, model?.Value, AiRequestKey.FileStamp(uploadName, uploadBytes)];
        AiOperationId operation = AiOperations.ImageEdit(new AiImageEditTaskId(task));
        string path = await RunImageAsync(
            request.RequestKeySeed,
            "image.edit",
            parts,
            token => availability.CheckAsync(new AiOperationAvailabilityRequest.Fixed(operation, model), token),
            (key, token) => editing.EditAsync(
                new AiImageEditRequest(
                    AiUploadSource.FromBytes(uploadName, uploadBytes),
                    new AiImageEditTaskId(task),
                    prompt,
                    model,
                    key),
                token),
            progress,
            cancellationToken);
        if (request.Prompt is { } typed)
            promptLibrary?.Record(request.Operation, typed);
        return new GenerativeExecutionResult(new Uri(path), model?.Value, null);
    }

    internal static byte[] ExpandCanvas(byte[] encodedPng, int expansionPercent)
    {
        using var input = new MemoryStream(encodedPng, writable: false);
        using Bitmap source = Bitmap.FromStream(input);
        (_, _, int horizontal, int vertical) =
            Beutl.ViewModels.Dialogs.AiImageEditDialogViewModel.GetOutpaintDimensions(
                source.Width,
                source.Height,
                expansionPercent);
        using Bitmap expanded = source.MakeBorder(vertical, vertical, horizontal, horizontal);
        using var output = new MemoryStream();
        expanded.Save(output, EncodedImageFormat.Png);
        return output.ToArray();
    }

    /// <summary>
    /// Sends one metered picture request through the dialogs' safeguards and saves what
    /// comes back next to the scene. The node's persisted seed makes the key stable across
    /// sessions: a request that never reported back is collected, not bought again.
    /// </summary>
    private async Task<string> RunImageAsync(
        string keySeed,
        string keyOperation,
        string?[] parts,
        Func<CancellationToken, Task<bool>> checkAvailability,
        Func<string, CancellationToken, Task<AiImageResult>> send,
        IProgress<GenerativeProgress> progress,
        CancellationToken cancellationToken)
    {
        using var requestKey = new AiRequestKey(seed: keySeed, operation: keyOperation);
        AiRequestName name = requestKey.NameFor(parts);
        try
        {
            progress.Report(new GenerativeProgress(Strings.AiGenerating));
            AiImageResult response = await AiMeteredDispatch.SendAsync(
                requestKey,
                name,
                null,
                checkAvailability,
                token => send(name.Key, token),
                n => requestKey.WithdrawAfterNoReservation(n),
                cancellationToken);

            // Past here the picture has been paid for; the key stays the way back to it.
            progress.Report(new GenerativeProgress(Beutl.Language.NodeGraphStrings.Generative_Loading));
            byte[] encoded = await AiImageResultDownload.DownloadEncodedAsync(
                content,
                response.ContentUri,
                cancellationToken);
            string path = await SaveAsync(encoded, ".png", cancellationToken);
            requestKey.Retire(name);
            return path;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (AiRequestFailure.Classify(ex) is { } failure)
        {
            if (failure.RetiresName)
                requestKey.Retire(name);
            if (failure.IsResultDownloadFailure)
                s_logger.LogError(ex, "Failed to download the AI result.");
            throw new GenerativeExecutionException(failure.Message, ex);
        }
        catch (Exception ex) when (ex is not GenerativeExecutionException)
        {
            s_logger.LogError(ex, "Failed to run a generative node.");
            throw new GenerativeExecutionException(Strings.AiUnexpectedError, ex);
        }
    }

    /// <summary>
    /// The named model when it can start, or — for a node left on the default — the model
    /// the dialog's picker would start on. Null when the catalog offers nothing, which lets
    /// the server pick, as the dialog does.
    /// </summary>
    private static GenerativeModelInfo? ResolveModel(string? modelId, IReadOnlyList<GenerativeModelInfo> offered)
    {
        if (modelId is not null)
        {
            GenerativeModelInfo? named = offered.FirstOrDefault(model => model.Id == modelId);
            if (offered.Count > 0 && named is not { IsAvailable: true })
                throw new GenerativeExecutionException(Strings.AiModelUnavailable);
            return named;
        }

        return offered.FirstOrDefault(model => model.IsAvailable && model.IsDefault)
            ?? offered.FirstOrDefault(model => model.IsAvailable);
    }

    private static void ReportPreview(AiImagePreview preview, IProgress<GenerativeProgress> progress)
    {
        try
        {
            using var stream = new MemoryStream(preview.Bytes.ToArray(), writable: false);
            AiImageDecodeValidator.ValidateEncoded(stream, AiRequestLimits.MaxImageUploadBytes);
            progress.Report(new GenerativeProgress(null, Ref<Bitmap>.Create(Bitmap.FromStream(stream))));
        }
        catch (Exception ex)
        {
            // A rough version that cannot be decoded is not worth a failure.
            s_logger.LogWarning(ex, "Failed to decode a partial AI image.");
        }
    }

    private async Task<string> SaveAsync(byte[] encoded, string extension, CancellationToken cancellationToken)
    {
        string directory = AiResultImporter.GetResourceDirectory(scene);
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, $"{Guid.NewGuid():N}{extension}");
        string temporary = Path.Combine(directory, $".{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, encoded, cancellationToken);
            File.Move(temporary, destination);
            return destination;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}
