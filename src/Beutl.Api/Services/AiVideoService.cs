using System.Diagnostics;
using System.Globalization;
using Beutl.Api.Clients;
using Refit;

namespace Beutl.Api.Services;

internal sealed partial class AiVideoService(
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
        if (request.InputReferences.Count > 0) return await CreateFromReferencesAsync(request, cancellationToken);
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
        StreamPart firstPart = AiMultipartFormData.File(
            firstStream,
            request.FirstFrame.FileName,
            request.FirstFrame.MediaType,
            "firstFrame");
        StreamPart? lastPart = request.LastFrame is null || lastStream is null
            ? null
            : AiMultipartFormData.File(
                lastStream,
                request.LastFrame.FileName,
                request.LastFrame.MediaType,
                "lastFrame");
        return await ExecuteAsync(
            "AiVideoService.CreateFromFrames",
            (authorization, token) => Application.Ai.CreateVideoFromFrames(
                authorization,
                idempotencyKey,
                firstPart,
                lastPart,
                null,
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
        return await AiUploadValidation.OpenAsync(
            source,
            AiRequestLimits.MaxFrameUploadBytes,
            cancellationToken);
    }
}
