using System.Diagnostics;
using System.Globalization;
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
                    },
                    requestToken),
            AiOperationAvailabilityRequest.Video videoRequest =>
                (authorization, requestToken) => application.Ai.CheckVideoAvailability(
                    authorization,
                    new AiVideoOperationAvailabilityRequestDto
                    {
                        Operation = videoRequest.Operation.Value,
                        DurationSeconds = videoRequest.DurationSeconds,
                    },
                    requestToken),
            AiOperationAvailabilityRequest.Transcription transcriptionRequest =>
                (authorization, requestToken) => application.Ai.CheckTranscriptionAvailability(
                    authorization,
                    new AiTranscriptionOperationAvailabilityRequestDto
                    {
                        Operation = transcriptionRequest.Operation.Value,
                        DurationSeconds = transcriptionRequest.DurationSeconds,
                    },
                    requestToken),
            AiOperationAvailabilityRequest.Translation translationRequest =>
                (authorization, requestToken) => application.Ai.CheckTranslationAvailability(
                    authorization,
                    new AiTranslationOperationAvailabilityRequestDto
                    {
                        Operation = translationRequest.Operation.Value,
                        CharacterCount = translationRequest.CharacterCount,
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
    public async Task<AiImageResult> GenerateAsync(
        AiImageGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string idempotencyKey = CreateIdempotencyKey();
        // The endpoint reads "auto" and an absent background the same way, so
        // only a transparent one is worth sending.
        string? background = request.TransparentBackground ? "transparent" : null;

        if (request.Reference is null)
        {
            return await ExecuteAsync(
                "AiImageGenerationService.Generate",
                (authorization, token) => Application.Ai.CreateImage(
                    authorization,
                    idempotencyKey,
                    new CreateAiImageRequest
                    {
                        Prompt = request.Prompt,
                        AspectRatio = request.AspectRatio.Value,
                        Background = background,
                        Seed = request.Seed,
                    },
                    token),
                AiModelMapper.ToModel,
                cancellationToken,
                activity => SetImageTags(activity, request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        await using Stream stream = await request.Reference.OpenReadAsync(cancellationToken);
        var referencePart = new StreamPart(
            stream,
            request.Reference.FileName,
            request.Reference.MediaType);
        return await ExecuteAsync(
            "AiImageGenerationService.GenerateFromReference",
            (authorization, token) => Application.Ai.CreateImageFromReference(
                authorization,
                idempotencyKey,
                referencePart,
                request.Prompt,
                request.AspectRatio.Value,
                background,
                request.Seed?.ToString(CultureInfo.InvariantCulture),
                token),
            AiModelMapper.ToModel,
            cancellationToken,
            activity => SetImageTags(activity, request));
    }

    private static void SetImageTags(Activity? activity, AiImageGenerationRequest request)
    {
        activity?.SetTag("aspectRatio", request.AspectRatio.Value);
        activity?.SetTag("transparentBackground", request.TransparentBackground);
        activity?.SetTag("hasReference", request.Reference is not null);
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
        string idempotencyKey = CreateIdempotencyKey();
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
        string idempotencyKey = CreateIdempotencyKey();
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
    {
        ArgumentNullException.ThrowIfNull(request);
        string idempotencyKey = CreateIdempotencyKey();
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
        };
        return ExecuteAsync(
            "AiCaptionTranslationService.Translate",
            (authorization, token) => Application.Ai.Translate(
                authorization,
                idempotencyKey,
                dto,
                token),
            AiModelMapper.ToModel,
            cancellationToken,
            activity =>
            {
                activity?.SetTag("segmentCount", request.Segments.Count);
                activity?.SetTag("targetLanguage", request.TargetLanguage);
            });
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
        string idempotencyKey = CreateIdempotencyKey();
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
                response.EnsureSuccessStatusCode();
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
