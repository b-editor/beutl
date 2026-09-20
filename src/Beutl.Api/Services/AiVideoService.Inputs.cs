using System.Globalization;
using Refit;

namespace Beutl.Api.Services;

internal sealed partial class AiVideoService
{
    public async Task<AiVideoGenerationResult> CreateFromSourceAsync(AiSourceVideoRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        string mode = request.Mode.ToString().ToLowerInvariant();
        string key = request.IdempotencyKey ?? CreateIdempotencyKey();
        await using Stream video = await AiUploadValidation.OpenAsync(request.SourceVideo, AiVideoInputLimits.MaxSourceBytes, cancellationToken);
        await using Stream? character = request.CharacterImage is null ? null
            : await AiUploadValidation.OpenAsync(request.CharacterImage, AiRequestLimits.MaxFrameUploadBytes, cancellationToken);
        return await ExecuteAsync("AiVideoService.FromSource",
            (authorization, token) => Application.Ai.CreateSourceVideo(authorization, key, mode,
                new StreamPart(video, request.SourceVideo.FileName, request.SourceVideo.MediaType),
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
        finally
        {
            await DisposeReferenceStreamsAsync(streams);
        }
    }
}
