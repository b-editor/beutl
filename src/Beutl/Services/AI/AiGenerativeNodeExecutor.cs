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
    IAiImageEditingService? editing = null,
    IAiVideoService? videos = null,
    IAiJobKindRegistry? jobKinds = null) : IGenerativeNodeExecutor, IGenerativeCostEstimator
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
            AiVideoGenerationNodeRequest video => GenerateVideoAsync(video, progress, cancellationToken),
            AiVideoEditNodeRequest edit => EditVideoAsync(edit, progress, cancellationToken),
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

    /// <summary>How long to wait between looks at a running clip, as the AI tab waits.</summary>
    internal TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);

    internal TimeSpan MaximumTransientPollDelay { get; init; } = TimeSpan.FromSeconds(30);

    private async Task<GenerativeExecutionResult> GenerateVideoAsync(
        AiVideoGenerationNodeRequest request,
        IProgress<GenerativeProgress> progress,
        CancellationToken cancellationToken)
    {
        if (videos is null || jobKinds is null)
            throw new GenerativeExecutionException(Beutl.Language.NodeGraphStrings.Generative_ExecutorUnavailable);

        (_, IReadOnlyList<GenerativeModelInfo> offered) =
            await models.LoadAsync(request.CatalogOperationId, cancellationToken);
        GenerativeModelInfo? chosen = ResolveModel(request.ModelId, offered);
        GenerativeVideoCapabilities limits = chosen?.Video ?? GenerativeVideoCapabilities.Unrestricted;
        AiModelId? model = chosen is not null ? new AiModelId(chosen.Id) : null;

        // Checked as the AI tab's controls check it, before anything is reserved.
        string prompt = request.Prompt;
        if (prompt.Length > Math.Min(limits.MaxPromptLength, AiRequestLimits.MaxPromptLength))
            throw new GenerativeExecutionException(AiPromptComposer.PromptTooLongMessage);
        if (!limits.DurationChoices.Contains(request.DurationSeconds)
            || !limits.ResolutionChoices.Contains(request.Resolution, StringComparer.Ordinal)
            || !limits.AspectRatioChoices.Contains(request.AspectRatio, StringComparer.Ordinal)
            || (request.FirstFrame is not null && !limits.SupportsFirstFrame)
            || (request.LastFrame is not null && !limits.SupportsLastFrame))
        {
            throw new GenerativeExecutionException(Strings.AiModelDoesNotSupportRequest);
        }

        var references = new List<(string Role, AiUploadSource Upload, byte[] Bytes)>();
        for (int i = 0; i < request.ImageReferences.Count; i++)
        {
            GenerativeImageInput image = request.ImageReferences[i];
            references.Add(($"reference-image-{i}", AiUploadSource.FromBytes(image.Name, "image/png", image.EncodedPng), image.EncodedPng));
        }

        for (int i = 0; i < request.VideoReferences.Count; i++)
        {
            GenerativeFileInput video = request.VideoReferences[i];
            references.Add(($"reference-video-{i}", AiUploadSource.FromBytes(video.Name, video.MediaType, video.Content), video.Content));
        }

        if (references.Count > 0
            && (!limits.SupportsInputReferences
                || request.ImageReferences.Count > limits.MaxImageReferences
                || request.VideoReferences.Count > limits.MaxVideoReferences
                || request.ImageReferences.Any(image => image.EncodedPng.LongLength > limits.MaxImageReferenceBytes)
                || request.VideoReferences.Any(video => video.Content.LongLength > limits.MaxVideoReferenceBytes)))
        {
            throw new GenerativeExecutionException(Strings.AiModelDoesNotSupportRequest);
        }

        if (request.FirstFrame is null && references.Count == 0 && !limits.SupportsPromptToVideo)
            throw new GenerativeExecutionException(Strings.AiChooseVideoInput);
        if (prompt.Length == 0)
            throw new GenerativeExecutionException(Strings.AiPromptRequired);
        try
        {
            AiVideoInputLimits.ValidateReferences(references.Select(reference => reference.Upload).ToArray());
        }
        catch (AiFileTooLargeException)
        {
            throw new GenerativeExecutionException(Strings.AiFileTooLarge);
        }
        catch (ArgumentException)
        {
            throw new GenerativeExecutionException(Strings.AiModelDoesNotSupportRequest);
        }

        // What a model does not take is left out, as the AI tab switches the control off.
        bool generateAudio = limits.SupportsAudio && request.GenerateAudio;
        int? seed = limits.SupportsSeed ? request.Seed : null;
        AiUploadSource? firstFrame = request.FirstFrame is { } first
            ? AiUploadSource.FromBytes(first.Name, first.EncodedPng)
            : null;
        AiUploadSource? lastFrame = request.LastFrame is { } last
            ? AiUploadSource.FromBytes(last.Name, last.EncodedPng)
            : null;

        // Laid out as the dialog lays out its key.
        string?[] parts =
        [
            prompt,
            request.DurationSeconds.ToString(CultureInfo.InvariantCulture),
            request.Resolution,
            request.AspectRatio,
            generateAudio ? "audio" : "silent",
            seed?.ToString(CultureInfo.InvariantCulture),
            model?.Value,
            request.FirstFrame is { } f ? AiRequestKey.ContentStamp(f.EncodedPng) : string.Empty,
            request.LastFrame is { } l ? AiRequestKey.ContentStamp(l.EncodedPng) : string.Empty,
        ];
        if (references.Count > 0)
        {
            parts =
            [
                .. parts,
                null,
                null,
                null,
                .. references.Select(reference =>
                    reference.Role + ":" + reference.Upload.MediaType + ":" + AiRequestKey.ContentStamp(reference.Bytes)),
            ];
        }

        using var requestKey = new AiRequestKey(seed: request.RequestKeySeed, operation: "video.generate");
        AiRequestName name = requestKey.NameFor(parts);
        try
        {
            progress.Report(new GenerativeProgress(Strings.AiVideoSubmitting));
            AiVideoGenerationResult response = await AiMeteredDispatch.SendAsync(
                requestKey,
                name,
                null,
                token => availability.CheckAsync(
                    new AiOperationAvailabilityRequest.Video(AiOperations.VideoGeneration, request.DurationSeconds, model),
                    token),
                token => videos.CreateAsync(
                    new AiVideoGenerationRequest(
                        prompt,
                        request.DurationSeconds,
                        new AiVideoResolutionId(request.Resolution),
                        new AiVideoAspectRatioId(request.AspectRatio),
                        generateAudio,
                        seed: seed,
                        firstFrame: firstFrame,
                        lastFrame: lastFrame,
                        model: model,
                        idempotencyKey: name.Key,
                        inputReferences: references.Select(reference => reference.Upload).ToArray()),
                    token),
                n => requestKey.WithdrawAfterNoReservation(n),
                cancellationToken);

            // Past here the clip has been reserved and paid for; the key is the way back.
            string path = await WaitAndSaveVideoAsync(response.JobId, requestKey, name, progress, cancellationToken);
            promptLibrary?.Record(request.Operation, prompt);
            return new GenerativeExecutionResult(new Uri(path), model?.Value, seed, IsVideo: true);
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

    /// <summary>Reads how long a clip lasts; replaced in tests, which have no decoder.</summary>
    internal Func<string, double> VideoDurationReader { get; init; } = static path =>
    {
        using var reader = Beutl.Media.Decoding.MediaReader.Open(
            path,
            new Beutl.Media.Decoding.MediaOptions(Beutl.Media.Decoding.MediaMode.Video));
        return reader.VideoInfo.Duration.ToDouble();
    };

    private async Task<GenerativeExecutionResult> EditVideoAsync(
        AiVideoEditNodeRequest request,
        IProgress<GenerativeProgress> progress,
        CancellationToken cancellationToken)
    {
        if (videos is null || jobKinds is null)
            throw new GenerativeExecutionException(Beutl.Language.NodeGraphStrings.Generative_ExecutorUnavailable);

        (_, IReadOnlyList<GenerativeModelInfo> offered) =
            await models.LoadAsync(request.CatalogOperationId, cancellationToken);
        GenerativeModelInfo? chosen = ResolveModel(request.ModelId, offered);
        GenerativeVideoCapabilities limits = chosen?.Video ?? GenerativeVideoCapabilities.Unrestricted;
        AiModelId? model = chosen is not null ? new AiModelId(chosen.Id) : null;
        (AiSourceVideoMode mode, AiOperationId operation) = request.Mode switch
        {
            AiVideoEditMode.Extend => (AiSourceVideoMode.Extend, AiOperations.VideoExtension),
            AiVideoEditMode.Motion => (AiSourceVideoMode.Motion, AiOperations.VideoMotion),
            _ => (AiSourceVideoMode.Edit, AiOperations.VideoEditing),
        };
        bool motion = mode == AiSourceVideoMode.Motion;

        string prompt = request.Prompt;
        if (prompt.Length > Math.Min(limits.MaxPromptLength, AiRequestLimits.MaxPromptLength))
            throw new GenerativeExecutionException(AiPromptComposer.PromptTooLongMessage);

        GenerativeFileInput source = request.SourceVideo;
        var sourceUpload = AiUploadSource.FromBytes(source.Name, source.MediaType, source.Content);
        AiUploadSource? characterUpload = request.CharacterImage is { } character
            ? AiUploadSource.FromBytes(character.Name, "image/png", character.EncodedPng)
            : null;
        double seconds = ReadDuration(source);
        // Checked as the AI tab checks the chosen files, before anything is reserved.
        try
        {
            AiVideoInputLimits.Validate(sourceUpload, "video", Math.Min(limits.MaxSourceVideoBytes, AiVideoInputLimits.MaxSourceBytes));
            if (motion)
            {
                if (characterUpload is null)
                    throw new GenerativeExecutionException(Strings.AiChooseCharacterImage);
                AiVideoInputLimits.Validate(characterUpload, "image", AiRequestLimits.MaxFrameUploadBytes);
            }
        }
        catch (AiFileTooLargeException)
        {
            throw new GenerativeExecutionException(Strings.AiFileTooLarge);
        }
        catch (ArgumentException)
        {
            throw new GenerativeExecutionException(Strings.AiVideoInputUnavailable);
        }

        if (!double.IsFinite(seconds) || seconds <= 0 || seconds > 60
            || seconds < (limits.MinSourceVideoSeconds ?? 0)
            || seconds > (limits.MaxSourceVideoSeconds ?? 60))
        {
            throw new GenerativeExecutionException(Strings.AiModelDoesNotSupportRequest);
        }

        // An edit keeps the clip's own length; the others take the chosen one.
        int durationSeconds = mode == AiSourceVideoMode.Edit
            ? (int)Math.Clamp(Math.Ceiling(seconds), 1, AiRequestLimits.MaxVideoDurationSeconds)
            : request.DurationSeconds;
        if (mode != AiSourceVideoMode.Edit && !limits.DurationChoices.Contains(durationSeconds))
            throw new GenerativeExecutionException(Strings.AiModelDoesNotSupportRequest);

        string orientation = request.Orientation == AiMotionOrientation.Image ? "image" : "video";
        string quality = request.Quality == AiMotionQuality.Pro ? "pro" : "standard";
        var inputs = new List<(string Role, AiUploadSource Upload, byte[] Bytes)>
        {
            ("source-video", sourceUpload, source.Content),
        };
        if (motion && characterUpload is not null)
            inputs.Add(("character-image", characterUpload, request.CharacterImage!.EncodedPng));

        // Laid out as the dialog lays out a source-video key: no shape, audio or seed.
        string?[] parts =
        [
            prompt,
            durationSeconds.ToString(CultureInfo.InvariantCulture),
            string.Empty,
            string.Empty,
            "silent",
            null,
            model?.Value,
            string.Empty,
            string.Empty,
            null,
            motion ? orientation : null,
            motion ? quality : null,
            .. inputs.Select(input => input.Role + ":" + input.Upload.MediaType + ":" + AiRequestKey.ContentStamp(input.Bytes)),
        ];

        string keyOperation = mode switch
        {
            AiSourceVideoMode.Extend => "video.extend",
            AiSourceVideoMode.Motion => "video.motion",
            _ => "video.edit",
        };
        using var requestKey = new AiRequestKey(seed: request.RequestKeySeed, operation: keyOperation);
        AiRequestName name = requestKey.NameFor(parts);
        try
        {
            progress.Report(new GenerativeProgress(Strings.AiVideoSubmitting));
            AiVideoGenerationResult response = await AiMeteredDispatch.SendAsync(
                requestKey,
                name,
                null,
                token => availability.CheckAsync(
                    new AiOperationAvailabilityRequest.Video(operation, durationSeconds, model),
                    token),
                token => videos.CreateFromSourceAsync(
                    new AiSourceVideoRequest(
                        mode,
                        prompt,
                        sourceUpload,
                        durationSeconds: mode == AiSourceVideoMode.Edit ? null : durationSeconds,
                        characterImage: motion ? characterUpload : null,
                        orientation: orientation,
                        quality: quality,
                        model: model,
                        idempotencyKey: name.Key),
                    token),
                n => requestKey.WithdrawAfterNoReservation(n),
                cancellationToken);

            string path = await WaitAndSaveVideoAsync(response.JobId, requestKey, name, progress, cancellationToken);
            promptLibrary?.Record(request.Operation, prompt);
            return new GenerativeExecutionResult(new Uri(path), model?.Value, null, IsVideo: true);
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

    // Probed from the bytes that will be uploaded, as the AI tab does: the file on disk
    // may change while the request is prepared.
    private double ReadDuration(GenerativeFileInput source)
    {
        (string path, FileStream stream) = AiTemporaryFileStore.Create(
            "inputs", "source-video", Path.GetExtension(source.Name));
        try
        {
            using (stream)
                stream.Write(source.Content);
            return VideoDurationReader(path);
        }
        catch (Exception ex) when (ex is not GenerativeExecutionException)
        {
            s_logger.LogWarning(ex, "Failed to read the length of a source clip.");
            throw new GenerativeExecutionException(Strings.AiVideoInputUnavailable, ex);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                s_logger.LogDebug(ex, "Failed to remove a probed source clip {Path}.", path);
            }
        }
    }

    /// <summary>
    /// Waits for a clip as the AI tab waits for it and saves it next to the scene. The name
    /// is retired only once the server has settled the job.
    /// </summary>
    private async Task<string> WaitAndSaveVideoAsync(
        AiJobId jobId,
        AiRequestKey requestKey,
        AiRequestName name,
        IProgress<GenerativeProgress> progress,
        CancellationToken cancellationToken)
    {
        (AiVideoJob job, AiJobStatusSemantics status) = await AiVideoJobWaiter.WaitAsync(
            videos!,
            jobKinds!,
            jobId,
            () => progress.Report(new GenerativeProgress(Strings.AiVideoProcessing)),
            PollInterval,
            MaximumTransientPollDelay,
            Task.Delay,
            cancellationToken);
        if (status.Outcome == AiJobOutcomes.Succeeded)
        {
            if (job.ContentUri is not { } contentUri)
                throw new InvalidOperationException("A successful video job did not provide content.");

            progress.Report(new GenerativeProgress(Beutl.Language.NodeGraphStrings.Generative_Loading));
            string downloaded = await AiVideoResultDownload.DownloadAsync(
                content, contentUri, job.ContentMetadata, cancellationToken);
            try
            {
                string directory = AiResultImporter.GetResourceDirectory(scene);
                Directory.CreateDirectory(directory);
                string destination = Path.Combine(directory, $"{Guid.NewGuid():N}{Path.GetExtension(downloaded)}");
                File.Move(downloaded, destination);
                requestKey.Retire(name);
                return destination;
            }
            catch
            {
                if (File.Exists(downloaded))
                    File.Delete(downloaded);
                throw;
            }
        }

        if (status.IsTerminal)
        {
            // Settled and refunded: the name would only ever answer with this failure.
            requestKey.Retire(name);
            throw new GenerativeExecutionException(AiErrorMessage.Localize(job.Error) ?? Strings.AiProviderError);
        }

        // An unknown status is not an outcome; the key stays so queueing again collects it.
        throw new GenerativeExecutionException(Strings.AiResultUnavailable);
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
    /// What a request is expected to cost, from the same catalog and availability check the
    /// run makes: the client knows the model's cost tier and whether the server would take it,
    /// not a price, which is all the AI tab shows as well.
    /// </summary>
    public async Task<GenerativeCostEstimate> EstimateAsync(
        GenerativeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        (_, IReadOnlyList<GenerativeModelInfo> offered) =
            await models.LoadAsync(request.CatalogOperationId, cancellationToken);
        GenerativeModelInfo? chosen;
        try
        {
            chosen = ResolveModel(request.ModelId, offered);
        }
        catch (GenerativeExecutionException)
        {
            return new GenerativeCostEstimate(request.ModelId, null, false);
        }

        AiModelId? model = chosen is not null ? new AiModelId(chosen.Id) : null;
        string? detail = null;
        AiOperationAvailabilityRequest? check;
        switch (request)
        {
            case AiImageGenerationNodeRequest:
                check = new AiOperationAvailabilityRequest.Fixed(AiOperations.ImageGeneration, model);
                break;
            case AiImageEditNodeRequest edit:
                check = new AiOperationAvailabilityRequest.Fixed(
                    AiOperations.ImageEdit(new AiImageEditTaskId(edit.Task.ToId())), model);
                break;
            case AiVideoGenerationNodeRequest video:
                detail = $"{video.DurationSeconds} {Strings.AiVideoSeconds}";
                check = new AiOperationAvailabilityRequest.Video(AiOperations.VideoGeneration, video.DurationSeconds, model);
                break;
            case AiVideoEditNodeRequest edit:
                int? seconds = edit.Mode == AiVideoEditMode.Edit ? TryReadRequestDuration(edit.SourceVideo) : edit.DurationSeconds;
                detail = seconds is { } s ? $"{s} {Strings.AiVideoSeconds}" : null;
                AiOperationId operation = edit.Mode switch
                {
                    AiVideoEditMode.Extend => AiOperations.VideoExtension,
                    AiVideoEditMode.Motion => AiOperations.VideoMotion,
                    _ => AiOperations.VideoEditing,
                };
                check = seconds is { } length
                    ? new AiOperationAvailabilityRequest.Video(operation, length, model)
                    : null;
                break;
            default:
                check = null;
                break;
        }

        bool? available = null;
        if (check is not null)
        {
            try
            {
                available = await availability.CheckAsync(check, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Unanswered is neither a go-ahead nor a shortfall; the run checks again.
                s_logger.LogWarning(ex, "Failed to check AI availability for a node estimate.");
            }
        }

        return new GenerativeCostEstimate(chosen?.Label ?? request.ModelId, detail, available);
    }

    private int? TryReadRequestDuration(GenerativeFileInput source)
    {
        try
        {
            return (int)Math.Clamp(Math.Ceiling(ReadDuration(source)), 1, AiRequestLimits.MaxVideoDurationSeconds);
        }
        catch (GenerativeExecutionException)
        {
            return null;
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
