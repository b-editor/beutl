using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Reactive.Bindings;
using Refit;

namespace Beutl.Api.Services;

internal sealed class AiEntitlementService(
    BeutlApiApplication application,
    AiEntitlementStore entitlementStore) : IAiEntitlementService
{
    public IReadOnlyReactiveProperty<AiEntitlements?> Entitlements
        => entitlementStore.Entitlements;

    public async Task<AiEntitlements?> RefreshAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource operationCts =
            application.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = operationCts.Token;
        token.ThrowIfCancellationRequested();
        using Activity? activity = application.ActivitySource.StartActivity(
            "AiEntitlementService.Refresh",
            ActivityKind.Client);

        AuthenticatedUser? authenticatedUser = application.AuthenticatedUser.Value;
        if (authenticatedUser is null)
            return null;

        await entitlementStore.WaitForBalanceRequestAsync(token);
        long snapshotRequest = entitlementStore.BeginSnapshotRequest();
        try
        {
            AuthenticatedApiResult<EntitlementsResponse> response =
                await application.SendAuthenticatedAsync(
                    (authorization, requestToken) =>
                        application.Ai.GetEntitlements(authorization, requestToken),
                    token,
                    authenticatedUser);
            AiEntitlements result = AiModelMapper.ToModel(response.Value);
            entitlementStore.ApplyEntitlements(result, response.User, snapshotRequest);
            return result;
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            entitlementStore.ApplyEntitlements(null, authenticatedUser, snapshotRequest);
            return null;
        }
        finally
        {
            entitlementStore.ReleaseBalanceRequest();
        }
    }
}

internal sealed class AiModelCatalogService(BeutlApiApplication application)
    : IAiModelCatalogService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AiModelCatalog? _catalog;

    public async Task<AiModelCatalog> GetAsync(CancellationToken cancellationToken)
    {
        if (_catalog is { } cached)
            return cached;

        using CancellationTokenSource operationCts =
            application.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = operationCts.Token;
        await _gate.WaitAsync(token);
        try
        {
            if (_catalog is { } raced)
                return raced;

            using Activity? activity = application.ActivitySource.StartActivity(
                "AiModelCatalogService.Get",
                ActivityKind.Client);
            if (application.AuthenticatedUser.Value is null)
                return AiModelCatalog.Empty;

            try
            {
                AuthenticatedApiResult<AiCapabilitiesResponse> response =
                    await application.SendAuthenticatedAsync(
                        (authorization, requestToken) =>
                            application.Ai.GetCapabilities(authorization, requestToken),
                        token,
                        application.AuthenticatedUser.Value);
                _catalog = AiModelMapper.ToModel(response.Value);
                return _catalog;
            }
            catch (ApiException ex)
            {
                // A dialog that cannot list the models still has to open. It
                // then offers no choice and the server uses its default, which
                // is exactly what this client did before it could choose.
                activity?.SetStatus(ActivityStatusCode.Error);
                activity?.SetTag("statusCode", (int)ex.StatusCode);
                return AiModelCatalog.Empty;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _catalog = null;
}

internal sealed class AiOperationAvailabilityService(BeutlApiApplication application)
    : IAiOperationAvailabilityService
{
    public async Task<bool> CheckAsync(
        AiOperationAvailabilityRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using CancellationTokenSource operationCts =
            application.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = operationCts.Token;
        token.ThrowIfCancellationRequested();
        using Activity? activity = application.ActivitySource.StartActivity(
            "AiOperationAvailabilityService.Check",
            ActivityKind.Client);
        activity?.SetTag("operation", request.Operation.Value);

        Func<string, CancellationToken, Task<AiOperationAvailabilityResponse>> send = request switch
        {
            AiOperationAvailabilityRequest.Fixed fixedRequest =>
                (authorization, requestToken) => application.Ai.CheckFixedAvailability(
                    authorization,
                    new AiFixedOperationAvailabilityRequestDto
                    {
                        Operation = fixedRequest.Operation.Value,
                        Model = fixedRequest.Model?.Value,
                    },
                    requestToken),
            AiOperationAvailabilityRequest.Video videoRequest =>
                (authorization, requestToken) => application.Ai.CheckVideoAvailability(
                    authorization,
                    new AiVideoOperationAvailabilityRequestDto
                    {
                        Operation = videoRequest.Operation.Value,
                        DurationSeconds = videoRequest.DurationSeconds,
                        Model = videoRequest.Model?.Value,
                    },
                    requestToken),
            AiOperationAvailabilityRequest.Transcription transcriptionRequest =>
                (authorization, requestToken) => application.Ai.CheckTranscriptionAvailability(
                    authorization,
                    new AiTranscriptionOperationAvailabilityRequestDto
                    {
                        Operation = transcriptionRequest.Operation.Value,
                        DurationSeconds = transcriptionRequest.DurationSeconds,
                        Model = transcriptionRequest.Model?.Value,
                    },
                    requestToken),
            AiOperationAvailabilityRequest.Translation translationRequest =>
                (authorization, requestToken) => application.Ai.CheckTranslationAvailability(
                    authorization,
                    new AiTranslationOperationAvailabilityRequestDto
                    {
                        Operation = translationRequest.Operation.Value,
                        CharacterCount = translationRequest.CharacterCount,
                        Model = translationRequest.Model?.Value,
                    },
                    requestToken),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

        try
        {
            AuthenticatedApiResult<AiOperationAvailabilityResponse> response =
                await application.SendAuthenticatedAsync(send, token);
            return response.Value.Available;
        }
        catch (ApiException ex)
        {
            throw await AiErrorConverter.ConvertAsync(ex, activity);
        }
    }
}

internal sealed class AiImageGenerationService(
    BeutlApiApplication application,
    AiJobChangeNotifier jobChangeNotifier)
    : AiMeteredCapabilityService(application, jobChangeNotifier),
        IAiImageGenerationService
{
    public Task<AiImageResult> GenerateAsync(
        AiImageGenerationRequest request,
        CancellationToken cancellationToken)
        => GenerateAsync(request, null, cancellationToken);

    public async Task<AiImageResult> GenerateAsync(
        AiImageGenerationRequest request,
        IProgress<AiImagePreview>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The caller's key when it has one: that is what lets a retry recover a
        // picture already paid for instead of buying it again.
        string idempotencyKey = request.IdempotencyKey ?? CreateIdempotencyKey();
        // The endpoint reads "auto" and an absent background the same way, so
        // leaving it to the model is sent as nothing at all.
        string backgroundValue = request.Background.Value;
        string? background =
            backgroundValue.Length > 0 && backgroundValue != "auto" ? backgroundValue : null;

        if (request.References.Count == 0)
        {
            var body = new CreateAiImageRequest
            {
                Prompt = request.Prompt,
                AspectRatio = request.AspectRatio.Value,
                Background = background,
                Seed = request.Seed,
                Model = request.Model?.Value,
            };
            if (progress is null)
            {
                return await ExecuteAsync(
                    "AiImageGenerationService.Generate",
                    (authorization, token) => Application.Ai.CreateImage(
                        authorization,
                        idempotencyKey,
                        body,
                        token),
                    AiModelMapper.ToModel,
                    cancellationToken,
                    activity => SetImageTags(activity, request));
            }

            return await ExecuteStreamingAsync(
                "AiImageGenerationService.Generate",
                () => JsonRequest("/api/v3/ai/images", idempotencyKey, body),
                item => ReportImagePreview(item, progress),
                data => AiModelMapper.ToModel(
                    JsonSerializer.Deserialize<AiImageResponse>(
                        data,
                        AiStreamJson.Options)
                    ?? throw new AiException("The AI image result was empty.")),
                cancellationToken,
                activity => SetImageTags(activity, request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        // Every reference is held open for the whole upload, so they are opened
        // together and closed together — one that failed to open must not leave
        // the ones before it behind.
        var streams = new List<Stream>(request.References.Count);
        try
        {
            var referenceParts = new List<StreamPart>(request.References.Count);
            foreach (AiUploadSource reference in request.References)
            {
                Stream stream = await reference.OpenReadAsync(cancellationToken);
                streams.Add(stream);
                referenceParts.Add(new StreamPart(
                    stream,
                    reference.FileName,
                    reference.MediaType));
            }

            return await ExecuteAsync(
                "AiImageGenerationService.GenerateFromReferences",
                (authorization, token) => Application.Ai.CreateImageFromReferences(
                    authorization,
                    idempotencyKey,
                    referenceParts,
                    request.Prompt,
                    request.AspectRatio.Value,
                    background,
                    request.Seed?.ToString(CultureInfo.InvariantCulture),
                    request.Model?.Value,
                    token),
                AiModelMapper.ToModel,
                cancellationToken,
                activity => SetImageTags(activity, request));
        }
        finally
        {
            foreach (Stream stream in streams)
            {
                await stream.DisposeAsync();
            }
        }
    }

    // A picture midway through being worked out. Anything that cannot be read as
    // one is passed over: it is a preview, and the finished picture is what the
    // caller is really waiting for.
    private static void ReportImagePreview(
        AiServerSentEvent item,
        IProgress<AiImagePreview> progress)
    {
        if (item.Event != "partial")
            return;

        AiImagePartialDto? partial =
            JsonSerializer.Deserialize<AiImagePartialDto>(item.Data, AiStreamJson.Options);
        if (partial is null || string.IsNullOrEmpty(partial.Image))
            return;

        byte[] bytes;
        try
        {
            bytes = System.Convert.FromBase64String(partial.Image);
        }
        catch (FormatException)
        {
            return;
        }

        if (bytes.Length > 0)
            progress.Report(new AiImagePreview(partial.Index, bytes));
    }

    private sealed record AiImagePartialDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("index")]
        public int Index { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("image")]
        public string? Image { get; init; }
    }

    private static void SetImageTags(Activity? activity, AiImageGenerationRequest request)
    {
        activity?.SetTag("aspectRatio", request.AspectRatio.Value);
        activity?.SetTag("background", request.Background.Value);
        activity?.SetTag("referenceCount", request.References.Count);
    }
}

internal sealed class AiImageEditingService(
    BeutlApiApplication application,
    AiJobChangeNotifier jobChangeNotifier)
    : AiMeteredCapabilityService(application, jobChangeNotifier),
        IAiImageEditingService
{
    public async Task<AiImageResult> EditAsync(
        AiImageEditRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The caller's key when it has one: that is what lets a retry recover an
        // edit already paid for instead of buying it again.
        string idempotencyKey = request.IdempotencyKey ?? CreateIdempotencyKey();
        cancellationToken.ThrowIfCancellationRequested();
        await using Stream stream = await request.Image.OpenReadAsync(cancellationToken);
        var filePart = new StreamPart(
            stream,
            request.Image.FileName,
            request.Image.MediaType);
        return await ExecuteAsync(
            "AiImageEditingService.Edit",
            (authorization, token) => Application.Ai.EditImage(
                authorization,
                idempotencyKey,
                filePart,
                request.Task.Value,
                request.Prompt,
                request.Model?.Value,
                token),
            AiModelMapper.ToModel,
            cancellationToken,
            activity => activity?.SetTag("task", request.Task.Value));
    }
}

internal sealed class AiTranscriptionService(
    BeutlApiApplication application,
    AiJobChangeNotifier jobChangeNotifier)
    : AiMeteredCapabilityService(application, jobChangeNotifier),
        IAiTranscriptionService
{
    public async Task<AiTranscriptionResponse> TranscribeAsync(
        AiTranscriptionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The caller's key when it has one: that is what lets a retry recover a
        // transcription already paid for instead of buying it again.
        string idempotencyKey = request.IdempotencyKey ?? CreateIdempotencyKey();
        cancellationToken.ThrowIfCancellationRequested();
        await using Stream stream = await request.Audio.OpenReadAsync(cancellationToken);
        var filePart = new StreamPart(
            stream,
            request.Audio.FileName,
            request.Audio.MediaType);
        return await ExecuteAsync(
            "AiTranscriptionService.Transcribe",
            (authorization, token) => Application.Ai.Transcribe(
                authorization,
                idempotencyKey,
                filePart,
                request.Language,
                request.Model?.Value,
                token),
            AiModelMapper.ToModel,
            cancellationToken);
    }

}

internal sealed class AiCaptionTranslationService(
    BeutlApiApplication application,
    AiJobChangeNotifier jobChangeNotifier)
    : AiMeteredCapabilityService(application, jobChangeNotifier),
        IAiCaptionTranslationService
{
    public Task<AiCaptionTranslationResponse> TranslateAsync(
        AiCaptionTranslationRequest request,
        CancellationToken cancellationToken)
        => TranslateAsync(request, null, cancellationToken);

    public Task<AiCaptionTranslationResponse> TranslateAsync(
        AiCaptionTranslationRequest request,
        IProgress<AiCaptionTranslationSegment>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The caller's key when it has one: that is what lets a retry recover a
        // translation already paid for instead of buying it again.
        string idempotencyKey = request.IdempotencyKey ?? CreateIdempotencyKey();
        var dto = new AiCaptionTranslationRequestDto
        {
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            Segments = request.Segments.Select(segment => new AiCaptionTranslationSegmentDto
            {
                Id = segment.Id,
                Text = segment.Text,
                Context = segment.Context is null
                    ? null
                    : new AiCaptionTranslationSegmentContextDto
                    {
                        GroupId = segment.Context.GroupId,
                        PartIndex = segment.Context.PartIndex,
                        Start = segment.Context.Start.TotalSeconds,
                        End = segment.Context.End.TotalSeconds,
                    },
            }).ToArray(),
            Style = request.Style is null
                ? null
                : new AiCaptionTranslationStyleDto
                {
                    Glossary = request.Style.Glossary,
                    MaxCharactersPerLine = request.Style.MaxCharactersPerLine,
                    MaxLines = request.Style.MaxLines,
                },
            Model = request.Model?.Value,
        };
        void Describe(Activity? activity)
        {
            activity?.SetTag("segmentCount", request.Segments.Count);
            activity?.SetTag("targetLanguage", request.TargetLanguage);
            activity?.SetTag("streamed", progress is not null);
        }

        if (progress is null)
        {
            return ExecuteAsync(
                "AiCaptionTranslationService.Translate",
                (authorization, token) => Application.Ai.Translate(
                    authorization,
                    idempotencyKey,
                    dto,
                    token),
                AiModelMapper.ToModel,
                cancellationToken,
                Describe);
        }

        return ExecuteStreamingAsync(
            "AiCaptionTranslationService.Translate",
            () => JsonRequest("/api/v3/ai/translations", idempotencyKey, dto),
            item =>
            {
                if (item.Event != "segment")
                    return;
                AiCaptionTranslationSegmentDto? segment =
                    JsonSerializer.Deserialize<AiCaptionTranslationSegmentDto>(
                        item.Data,
                        AiStreamJson.Options);
                if (segment is not null)
                {
                    progress.Report(new AiCaptionTranslationSegment
                    {
                        Id = segment.Id,
                        Text = segment.Text,
                    });
                }
            },
            data => AiModelMapper.ToModel(
                JsonSerializer.Deserialize<AiCaptionTranslationResponseDto>(
                    data,
                    AiStreamJson.Options)
                ?? throw new AiException("The AI translation result was empty.")),
            cancellationToken,
            Describe);
    }
}

internal sealed class AiVideoService(
    BeutlApiApplication application,
    AiJobChangeNotifier jobChangeNotifier)
    : AiMeteredCapabilityService(application, jobChangeNotifier), IAiVideoService
{
    public async Task<AiVideoGenerationResult> CreateAsync(
        AiVideoGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        // The caller's key when it has one: that is what lets a retry recover a
        // clip already paid for instead of buying it again.
        string idempotencyKey = request.IdempotencyKey ?? CreateIdempotencyKey();
        if (request.FirstFrame is null)
        {
            return await ExecuteAsync(
                "AiVideoService.Create",
                (authorization, token) => Application.Ai.CreateVideo(
                    authorization,
                    idempotencyKey,
                    new CreateAiVideoRequest
                    {
                        Prompt = request.Prompt,
                        DurationSeconds = request.DurationSeconds,
                        Resolution = request.Resolution.Value,
                        AspectRatio = request.AspectRatio.Value,
                        GenerateAudio = request.GenerateAudio,
                        Seed = request.Seed,
                        Model = request.Model?.Value,
                    },
                    token),
                AiModelMapper.ToModel,
                cancellationToken,
                activity => SetVideoTags(activity, request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        await using Stream firstStream = await OpenFrameStreamAsync(
            request.FirstFrame,
            cancellationToken);
        await using Stream? lastStream = request.LastFrame is null
            ? null
            : await OpenFrameStreamAsync(request.LastFrame, cancellationToken);
        var firstPart = new StreamPart(
            firstStream,
            request.FirstFrame.FileName,
            request.FirstFrame.MediaType);
        StreamPart? lastPart = request.LastFrame is null || lastStream is null
            ? null
            : new StreamPart(
                lastStream,
                request.LastFrame.FileName,
                request.LastFrame.MediaType);
        return await ExecuteAsync(
            "AiVideoService.CreateFromFrames",
            (authorization, token) => Application.Ai.CreateVideoFromFrames(
                authorization,
                idempotencyKey,
                firstPart,
                lastPart,
                request.Prompt,
                request.DurationSeconds,
                request.Resolution.Value,
                request.AspectRatio.Value,
                request.GenerateAudio ? "true" : "false",
                request.Seed?.ToString(CultureInfo.InvariantCulture),
                request.Model?.Value,
                token),
            AiModelMapper.ToModel,
            cancellationToken,
            activity => SetVideoTags(activity, request));
    }

    public Task<AiVideoJob> GetAsync(
        AiJobId jobId,
        CancellationToken cancellationToken)
    {
        if (jobId.Value.Length == 0)
            throw new ArgumentException("A job identifier is required.", nameof(jobId));
        return ExecuteAsync(
            "AiVideoService.Get",
            (authorization, token) => Application.Ai.GetVideoJob(
                authorization,
                jobId.Value,
                token),
            AiModelMapper.ToModel,
            cancellationToken,
            activity => activity?.SetTag("jobId", jobId.Value),
            notifyJobsChanged: false);
    }

    private static void SetVideoTags(Activity? activity, AiVideoGenerationRequest request)
    {
        activity?.SetTag("durationSeconds", request.DurationSeconds);
        activity?.SetTag("resolution", request.Resolution.Value);
        activity?.SetTag("aspectRatio", request.AspectRatio.Value);
        activity?.SetTag("generateAudio", request.GenerateAudio);
        activity?.SetTag("hasFirstFrame", request.FirstFrame is not null);
        activity?.SetTag("hasLastFrame", request.LastFrame is not null);
    }

    private static async ValueTask<Stream> OpenFrameStreamAsync(
        AiUploadSource source,
        CancellationToken cancellationToken)
    {
        Stream stream = await source.OpenReadAsync(cancellationToken);
        try
        {
            if (source.Length > AiRequestLimits.MaxFrameUploadBytes
                || stream.CanSeek
                && stream.Length - stream.Position > AiRequestLimits.MaxFrameUploadBytes)
            {
                throw new AiFileTooLargeException();
            }

            if (stream.CanSeek)
                return stream;

            var buffered = new MemoryStream();
            try
            {
                byte[] buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    int read = await stream.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                        break;
                    total += read;
                    if (total > AiRequestLimits.MaxFrameUploadBytes)
                        throw new AiFileTooLargeException();
                    await buffered.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                buffered.Position = 0;
                await stream.DisposeAsync();
                return buffered;
            }
            catch
            {
                await buffered.DisposeAsync();
                throw;
            }
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }
}

internal sealed class AuthenticatedContentService(BeutlApiApplication application)
    : IAuthenticatedContentService
{
    public async Task<AiContentDownload> CopyToAsync(
        Uri contentUri,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contentUri);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        Uri downloadUri = ValidateContentUri(contentUri);
        using CancellationTokenSource operationCts =
            application.CreateLifetimeLinkedTokenSource(cancellationToken);
        AuthenticatedApiResult<AiContentDownload> result =
            await application.SendAuthenticatedAsync(
            async (authorization, requestToken) =>
            {
                using HttpRequestMessage request = new(HttpMethod.Get, downloadUri);
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
                using HttpResponseMessage response =
                    await application.HttpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        requestToken);
                if (!response.IsSuccessStatusCode)
                {
                    // Said as its own failure rather than as a failed request:
                    // by the time a result is fetched the job has run and been
                    // charged for, and the caller has somewhere to send the
                    // user for it.
                    throw new AiContentUnavailableException((int)response.StatusCode);
                }
                string? fileName = NormalizeContentDispositionFileName(
                    response.Content.Headers.ContentDisposition);
                string? contentType = response.Content.Headers.ContentType?.MediaType;
                AiContentMetadata? metadata = string.IsNullOrWhiteSpace(fileName)
                    && string.IsNullOrWhiteSpace(contentType)
                        ? null
                        : new AiContentMetadata(fileName, contentType);
                await using Stream source = await response.Content.ReadAsStreamAsync(requestToken);
                await source.CopyToAsync(destination, requestToken);
                return new AiContentDownload(metadata);
            },
            operationCts.Token);
        return result.Value;
    }

    private Uri ValidateContentUri(Uri contentUri)
    {
        Uri? baseAddress = application.HttpClient.BaseAddress;
        if (!contentUri.IsAbsoluteUri
            || baseAddress is null
            || !string.Equals(contentUri.Scheme, baseAddress.Scheme, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(contentUri.IdnHost, baseAddress.IdnHost, StringComparison.OrdinalIgnoreCase)
            || contentUri.Port != baseAddress.Port
            || !contentUri.AbsolutePath.StartsWith("/api/contents/", StringComparison.Ordinal)
            || contentUri.AbsolutePath.Length == "/api/contents/".Length)
        {
            throw new ArgumentException(
                "The URI must identify Beutl content on the configured API origin.",
                nameof(contentUri));
        }

        return contentUri;
    }

    private static string? NormalizeContentDispositionFileName(
        System.Net.Http.Headers.ContentDispositionHeaderValue? disposition)
    {
        if (disposition is null)
            return null;

        string? encoded = disposition.FileNameStar;
        if (!string.IsNullOrWhiteSpace(encoded))
        {
            try
            {
                encoded = Uri.UnescapeDataString(encoded);
            }
            catch (UriFormatException ex)
            {
                throw new AiException("The AI response contains an invalid content filename.", ex);
            }
        }

        return (encoded ?? disposition.FileName)?.Trim().Trim('"');
    }
}

internal abstract class AiMeteredCapabilityService(
    BeutlApiApplication application,
    AiJobChangeNotifier jobChangeNotifier)
{
    protected BeutlApiApplication Application { get; } = application;

    protected static string CreateIdempotencyKey() => Guid.NewGuid().ToString("D");

    /// <summary>
    /// Runs a request that answers a piece at a time, handing each piece to the
    /// caller and returning what the closing event carries.
    /// </summary>
    /// <remarks>
    /// Everything the server can refuse before it starts working still comes
    /// back as an ordinary status code, and is converted here exactly as a
    /// Refit reply would be. Once the stream is open the answer is one of two
    /// events; a stream that carries neither was cut off, and whatever it was
    /// carrying may well have finished and been charged for, which is why that
    /// is said in its own way rather than as a plain failure.
    /// </remarks>
    protected async Task<TResult> ExecuteStreamingAsync<TResult>(
        string activityName,
        Func<HttpRequestMessage> createRequest,
        Action<AiServerSentEvent> onProgress,
        Func<string, TResult> readResult,
        CancellationToken cancellationToken,
        Action<Activity?>? configureActivity = null,
        bool notifyJobsChanged = true)
    {
        using CancellationTokenSource operationCts =
            Application.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = operationCts.Token;
        token.ThrowIfCancellationRequested();
        using Activity? activity = Application.ActivitySource.StartActivity(
            activityName,
            ActivityKind.Client);
        configureActivity?.Invoke(activity);

        AuthenticatedApiResult<TResult> response = await Application.SendAuthenticatedAsync(
            async (authorization, requestToken) =>
            {
                using HttpRequestMessage request = createRequest();
                request.Headers.TryAddWithoutValidation("Authorization", authorization);
                request.Headers.Accept.Add(
                    new MediaTypeWithQualityHeaderValue(AiEventStream.MediaType));
                using HttpResponseMessage message = await Application.HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    requestToken);
                if (!message.IsSuccessStatusCode)
                {
                    throw await ToFailureAsync(message, activity, requestToken);
                }

                if (!AiEventStream.IsEventStream(message))
                {
                    // A server that does not stream answers the same request in
                    // one piece, and that answer is the same shape the closing
                    // event would have carried. There is simply nothing to show
                    // on the way.
                    return readResult(await message.Content.ReadAsStringAsync(requestToken));
                }

                await using Stream stream = await message.Content.ReadAsStreamAsync(requestToken);
                await foreach (AiServerSentEvent item in
                    AiEventStream.ReadAsync(stream, requestToken))
                {
                    switch (item.Event)
                    {
                        case AiEventStream.ResultEvent:
                            return readResult(item.Data);
                        case AiEventStream.ErrorEvent:
                            activity?.SetStatus(ActivityStatusCode.Error);
                            throw AiErrorConverter.Convert(
                                500,
                                DeserializeError(item.Data),
                                null);
                        default:
                            onProgress(item);
                            break;
                    }
                }

                activity?.SetStatus(ActivityStatusCode.Error);
                throw new AiRequestInterruptedException();
            },
            token);

        if (notifyJobsChanged)
        {
            jobChangeNotifier.Notify();
        }

        return response.Value;
    }

    private static async Task<AiException> ToFailureAsync(
        HttpResponseMessage message,
        Activity? activity,
        CancellationToken cancellationToken)
    {
        activity?.SetStatus(ActivityStatusCode.Error);
        string body = string.Empty;
        try
        {
            body = await message.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception)
        {
            // A body that cannot be read says nothing more than the status did.
        }

        return AiErrorConverter.Convert(
            (int)message.StatusCode,
            DeserializeError(body),
            null,
            $"The AI request failed ({(int)message.StatusCode}).");
    }

    /// <summary>The same JSON body Refit would have sent, as a raw request.</summary>
    protected HttpRequestMessage JsonRequest<TBody>(
        string path,
        string idempotencyKey,
        TBody body)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(Application.HttpClient.BaseAddress!, path))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, AiStreamJson.Options),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return request;
    }

    // Read leniently: what matters is the code and the message, and a body that
    // carries them but not every field the client's own record insists on is
    // still the failure it says it is.
    private sealed record ErrorBody
    {
        [System.Text.Json.Serialization.JsonPropertyName("error_code")]
        public ApiErrorCode? ErrorCode { get; init; }

        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string? Message { get; init; }
    }

    private static ApiErrorResponse? DeserializeError(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            ErrorBody? parsed = JsonSerializer.Deserialize<ErrorBody>(
                body,
                AiStreamJson.Options);
            if (parsed is null)
                return null;
            return new ApiErrorResponse
            {
                ErrorCode = parsed.ErrorCode ?? ApiErrorCode.Unknown,
                Message = parsed.Message,
                DocumentationUrl = null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    protected async Task<TResult> ExecuteAsync<TResponse, TResult>(
        string activityName,
        Func<string, CancellationToken, Task<TResponse>> send,
        Func<TResponse, TResult> map,
        CancellationToken cancellationToken,
        Action<Activity?>? configureActivity = null,
        bool notifyJobsChanged = true)
    {
        using CancellationTokenSource operationCts =
            Application.CreateLifetimeLinkedTokenSource(cancellationToken);
        CancellationToken token = operationCts.Token;
        token.ThrowIfCancellationRequested();
        using Activity? activity = Application.ActivitySource.StartActivity(
            activityName,
            ActivityKind.Client);
        configureActivity?.Invoke(activity);

        try
        {
            AuthenticatedApiResult<TResponse> response =
                await Application.SendAuthenticatedAsync(send, token);
            TResult result = map(response.Value);
            if (notifyJobsChanged)
            {
                jobChangeNotifier.Notify();
            }
            return result;
        }
        catch (ApiException ex)
        {
            throw await AiErrorConverter.ConvertAsync(ex, activity);
        }
    }
}

internal static class AiMediaTypes
{
    public static string Get(string filePath)
    {
        return Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".avif" => "image/avif",
            ".heif" or ".heic" => "image/heif",
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".flac" => "audio/flac",
            ".m4a" => "audio/mp4",
            ".ogg" or ".oga" or ".opus" => "audio/ogg",
            ".webm" => "audio/webm",
            ".aac" => "audio/aac",
            ".mp4" or ".m4v" => "video/mp4",
            ".mov" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            _ => "application/octet-stream",
        };
    }
}
