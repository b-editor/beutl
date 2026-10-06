using System.Diagnostics;
using System.Net.Http.Headers;
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
        await using Stream stream = await AiUploadValidation.OpenAsync(
            request.Image,
            AiRequestLimits.MaxImageUploadBytes,
            cancellationToken);
        StreamPart filePart = AiMultipartFormData.File(
            stream,
            request.Image.FileName,
            request.Image.MediaType,
            "file");
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
        await using Stream stream = await AiUploadValidation.OpenAsync(
            request.Audio,
            AiRequestLimits.MaxTranscriptionUploadBytes,
            cancellationToken);
        return await ExecuteStreamingAsync(
            "AiTranscriptionService.Transcribe",
            () =>
            {
                var body = new MultipartFormDataContent();
                var file = new StreamContent(stream);
                file.Headers.ContentType = MediaTypeHeaderValue.Parse(request.Audio.MediaType);
                body.Add(file, "\"file\"", AiMultipartFormData.LegacyFileName(request.Audio.FileName));
                file.Headers.ContentDisposition!.FileNameStar = request.Audio.FileName;
                if (request.Language is not null)
                    body.Add(new StringContent(request.Language), "\"language\"");
                if (request.Model is { } model)
                    body.Add(new StringContent(model.Value), "\"model\"");
                var message = new HttpRequestMessage(HttpMethod.Post, "/api/v3/ai/transcriptions") { Content = body };
                message.Headers.TryAddWithoutValidation(IdempotencyKeyHeader, idempotencyKey);
                return message;
            },
            _ => { },
            ReadTranscriptionResult,
            cancellationToken,
            requestEventStream: false);
    }

    internal static AiTranscriptionResponse ReadTranscriptionResult(string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("segments", out JsonElement segments)
                || segments.ValueKind != JsonValueKind.Array || segments.GetArrayLength() > 10000)
                throw new AiProviderErrorException(new InvalidDataException("The transcription result has an invalid or excessive segment count."));
            if (document.RootElement.TryGetProperty("words", out JsonElement words)
                && words.ValueKind != JsonValueKind.Null
                && (words.ValueKind != JsonValueKind.Array || words.GetArrayLength() > 100000))
                throw new AiProviderErrorException(new InvalidDataException("The transcription result has an invalid or excessive word count."));
            return AiModelMapper.ToModel(
                JsonSerializer.Deserialize<AiTranscriptionResponseDto>(body, AiStreamJson.Options)
                ?? throw new AiProviderErrorException(new InvalidDataException("The transcription result was empty.")));
        }
        catch (JsonException ex)
        {
            throw new AiProviderErrorException(ex);
        }
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
        AiCaptionTranslationRequestPayload payload =
            AiCaptionTranslationRequestTransport.CreatePayload(request);
        // The caller's key when it has one: that is what lets a retry recover a
        // translation already paid for instead of buying it again.
        string idempotencyKey = request.IdempotencyKey ?? CreateIdempotencyKey();
        void Describe(Activity? activity)
        {
            activity?.SetTag("segmentCount", request.Segments.Count);
            activity?.SetTag("targetLanguage", request.TargetLanguage);
            activity?.SetTag("streamed", progress is not null);
        }

        if (progress is null)
        {
            return ExecuteStreamingAsync(
                "AiCaptionTranslationService.Translate",
                () => JsonRequestBytes(
                    "/api/v3/ai/translations",
                    idempotencyKey,
                    payload.Json),
                _ => { },
                ReadTranslationResult,
                cancellationToken,
                Describe,
                requestEventStream: false);
        }

        HashSet<string> requestedSegmentIds = request.Segments
            .Select(static segment => segment.Id)
            .ToHashSet(StringComparer.Ordinal);
        var reportedSegmentIds = new HashSet<string>(StringComparer.Ordinal);
        return ExecuteStreamingAsync(
            "AiCaptionTranslationService.Translate",
            () => JsonRequestBytes("/api/v3/ai/translations", idempotencyKey, payload.Json),
            item => ReportTranslationSegment(
                item,
                progress,
                requestedSegmentIds,
                reportedSegmentIds),
            ReadTranslationResult,
            cancellationToken,
            Describe);
    }

    private static AiCaptionTranslationResponse ReadTranslationResult(string data)
        => AiModelMapper.ToModel(
            JsonSerializer.Deserialize<AiCaptionTranslationResponseDto>(
                data,
                AiStreamJson.Options)
            ?? throw new AiException("The AI translation result was empty."));

    private static void ReportTranslationSegment(
        AiServerSentEvent item,
        IProgress<AiCaptionTranslationSegment> progress,
        IReadOnlySet<string> requestedSegmentIds,
        ISet<string> reportedSegmentIds)
    {
        if (item.Event != "segment")
            return;

        AiCaptionTranslationSegmentDto? segment;
        try
        {
            segment = JsonSerializer.Deserialize<AiCaptionTranslationSegmentDto>(
                item.Data,
                AiStreamJson.Options);
        }
        catch (JsonException)
        {
            return;
        }

        if (segment is null
            || !AiRequestLimits.IsSafeTranslationIdentifier(segment.Id)
            || string.IsNullOrWhiteSpace(segment.Text)
            || segment.Text.Length > AiRequestLimits.MaxTranslationCharacters
            || !requestedSegmentIds.Contains(segment.Id)
            || !reportedSegmentIds.Add(segment.Id))
        {
            return;
        }

        progress.Report(new AiCaptionTranslationSegment
        {
            Id = segment.Id,
            Text = segment.Text,
        });
    }
}
