using System.Globalization;
using Refit;

namespace Beutl.Api.Services;

internal sealed partial class AiVideoService
{
    public Task<IReadOnlyList<AiSourceVideoOption>> GetSourcesAsync(CancellationToken cancellationToken)
        => ExecuteAsync<AiSourceVideoPage, IReadOnlyList<AiSourceVideoOption>>(
            "AiVideoService.Sources",
            (authorization, token) => Application.Ai.GetSourceVideos(authorization, token),
            page => (page.Videos ?? []).Where(video => Guid.TryParse(video.JobId, out _)
                && double.IsFinite(video.DurationSeconds) && video.DurationSeconds > 0
                && video.DurationSeconds <= AiRequestLimits.MaxVideoDurationSeconds).ToArray(),
            cancellationToken, notifyJobsChanged: false);

    public async Task<AiVideoGenerationResult> CreateFromSourceAsync(AiSourceVideoRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string mode = request.Mode.ToString().ToLowerInvariant();
        string key = request.IdempotencyKey ?? CreateIdempotencyKey();
        if (request.SourceJobId is { } job && request.Mode != AiSourceVideoMode.Motion)
        {
            var body = new Dictionary<string, object> { ["sourceJobId"] = job.Value, ["prompt"] = request.Prompt };
            if (request.DurationSeconds is { } seconds) body["durationSeconds"] = seconds;
            if (request.Model is { } model) body["model"] = model.Value;
            return await ExecuteAsync("AiVideoService.FromJob",
                (authorization, token) => Application.Ai.CreateSourceVideoFromJob(authorization, key, mode, body, token),
                AiModelMapper.ToModel, cancellationToken);
        }
        await using Stream? video = request.SourceVideo is null ? null
            : await AiUploadValidation.OpenAsync(request.SourceVideo, AiVideoInputLimits.MaxSourceBytes, cancellationToken);
        await using Stream? character = request.CharacterImage is null ? null
            : await AiUploadValidation.OpenAsync(request.CharacterImage, AiRequestLimits.MaxFrameUploadBytes, cancellationToken);
        return await ExecuteAsync("AiVideoService.FromSource",
            (authorization, token) => Application.Ai.CreateSourceVideo(authorization, key, mode,
                video is null ? null : new StreamPart(video, request.SourceVideo!.FileName, request.SourceVideo.MediaType),
                request.SourceJobId?.Value,
                character is null ? null : new StreamPart(character, request.CharacterImage!.FileName, request.CharacterImage.MediaType),
                request.Prompt, request.DurationSeconds?.ToString(CultureInfo.InvariantCulture),
                request.Mode == AiSourceVideoMode.Motion ? request.Orientation : null,
                request.Mode == AiSourceVideoMode.Motion ? request.Quality : null, request.Model?.Value, token),
            AiModelMapper.ToModel, cancellationToken);
    }

    private async Task<AiVideoGenerationResult> CreateFromReferencesAsync(AiVideoGenerationRequest request, CancellationToken cancellationToken)
    {
        string key = request.IdempotencyKey ?? CreateIdempotencyKey();
        var streams = new List<Stream>();
        try
        {
            var parts = new List<StreamPart>();
            foreach (var source in request.InputReferences)
            {
                var stream = await AiUploadValidation.OpenAsync(source, AiVideoInputLimits.MaxSourceBytes, cancellationToken);
                streams.Add(stream);
                parts.Add(new(stream, source.FileName, source.MediaType));
            }
            return await ExecuteAsync("AiVideoService.References",
                (authorization, token) => Application.Ai.CreateVideoFromFrames(authorization,
                    key, null, null, parts,
                    request.Prompt, request.DurationSeconds, request.Resolution.Value, request.AspectRatio.Value,
                    request.GenerateAudio ? "true" : "false", request.Seed?.ToString(CultureInfo.InvariantCulture), request.Model?.Value, token),
                AiModelMapper.ToModel, cancellationToken);
        }
        finally { foreach (var stream in streams) await stream.DisposeAsync(); }
    }
}
