using System.Diagnostics;
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

internal sealed class AiImageGenerationService(
    BeutlApiApplication application,
    AiJobChangeNotifier jobChangeNotifier)
    : AiMeteredCapabilityService(application, jobChangeNotifier),
        IAiImageGenerationService
{
    public Task<AiImageResult> GenerateAsync(
        AiImageGenerationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ExecuteAsync(
            "AiImageGenerationService.Generate",
            (authorization, token) => Application.Ai.CreateImage(
                authorization,
                new CreateAiImageRequest
                {
                    Prompt = request.Prompt,
                    Size = request.Size.Value,
                },
                token),
            AiModelMapper.ToModel,
            cancellationToken,
            activity => activity?.SetTag("size", request.Size.Value));
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
        };
        return ExecuteAsync(
            "AiCaptionTranslationService.Translate",
            (authorization, token) => Application.Ai.Translate(authorization, dto, token),
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
        if (request.FirstFrame is null)
        {
            return await ExecuteAsync(
                "AiVideoService.Create",
                (authorization, token) => Application.Ai.CreateVideo(
                    authorization,
                    new CreateAiVideoRequest
                    {
                        Prompt = request.Prompt,
                        DurationSeconds = request.DurationSeconds,
                        Resolution = request.Resolution.Value,
                    },
                    token),
                AiModelMapper.ToModel,
                cancellationToken,
                activity => SetVideoTags(activity, request));
        }

        cancellationToken.ThrowIfCancellationRequested();
        await using Stream firstStream = await request.FirstFrame.OpenReadAsync(cancellationToken);
        await using Stream? lastStream = request.LastFrame is null
            ? null
            : await request.LastFrame.OpenReadAsync(cancellationToken);
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
                firstPart,
                lastPart,
                request.Prompt,
                request.DurationSeconds,
                request.Resolution.Value,
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
        activity?.SetTag("hasFirstFrame", request.FirstFrame is not null);
        activity?.SetTag("hasLastFrame", request.LastFrame is not null);
    }
}

internal sealed class AuthenticatedContentService(BeutlApiApplication application)
    : IAuthenticatedContentService
{
    public async Task CopyToAsync(
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
                await using Stream source = await response.Content.ReadAsStreamAsync(requestToken);
                await source.CopyToAsync(destination, requestToken);
                return true;
            },
            operationCts.Token);
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
}

internal abstract class AiMeteredCapabilityService(
    BeutlApiApplication application,
    AiJobChangeNotifier jobChangeNotifier)
{
    protected BeutlApiApplication Application { get; } = application;

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
