using System.Text.Json.Serialization;

namespace Beutl.Api.Services;

public enum AiSourceVideoMode { Edit, Extend, Motion }

public sealed record AiSourceVideoRequest
{
    public AiSourceVideoRequest(AiSourceVideoMode mode, string prompt,
        AiUploadSource? sourceVideo = null, AiJobId? sourceJobId = null,
        int? durationSeconds = null, AiUploadSource? characterImage = null,
        string orientation = "video", string quality = "standard",
        AiModelId? model = null, string? idempotencyKey = null)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if ((sourceVideo is null) == (sourceJobId is null))
            throw new ArgumentException("Exactly one source video is required.");
        if (sourceJobId is { } job && !Guid.TryParse(job.Value, out _))
            throw new ArgumentException("The source job must be a UUID.", nameof(sourceJobId));
        if (mode == AiSourceVideoMode.Edit && durationSeconds is not null)
            throw new ArgumentException("An edit follows the source duration.", nameof(durationSeconds));
        if (mode != AiSourceVideoMode.Edit)
            AiRequestLimits.ValidateVideoDurationSeconds(durationSeconds ?? 0, nameof(durationSeconds));
        if ((mode == AiSourceVideoMode.Motion) != (characterImage is not null))
            throw new ArgumentException("Only motion control requires a character image.");
        if (orientation is not ("image" or "video") || quality is not ("standard" or "pro"))
            throw new ArgumentException("Unsupported motion options.");
        if (sourceVideo is not null) AiVideoInputLimits.Validate(sourceVideo, "video", AiVideoInputLimits.MaxSourceBytes);
        if (characterImage is not null) AiVideoInputLimits.Validate(characterImage, "image", AiRequestLimits.MaxFrameUploadBytes);
        Mode = mode;
        Prompt = AiRequestLimits.ValidatePrompt(prompt, nameof(prompt));
        SourceVideo = sourceVideo;
        SourceJobId = sourceJobId;
        DurationSeconds = durationSeconds;
        CharacterImage = characterImage;
        Orientation = orientation;
        Quality = quality;
        Model = AiRequestLimits.ValidateOptionalModel(model, nameof(model));
        IdempotencyKey = AiRequestLimits.ValidateOptionalIdempotencyKey(idempotencyKey, nameof(idempotencyKey));
    }

    public AiSourceVideoMode Mode { get; }
    public string Prompt { get; }
    public AiUploadSource? SourceVideo { get; }
    public AiJobId? SourceJobId { get; }
    public int? DurationSeconds { get; }
    public AiUploadSource? CharacterImage { get; }
    public string Orientation { get; }
    public string Quality { get; }
    public AiModelId? Model { get; }
    public string? IdempotencyKey { get; }
}

public sealed record AiSourceVideoOption(
    [property: JsonPropertyName("jobId")] string JobId,
    [property: JsonPropertyName("fileName")] string? FileName,
    [property: JsonPropertyName("durationSeconds")] double DurationSeconds,
    [property: JsonPropertyName("createdAt")] DateTimeOffset CreatedAt);

internal sealed record AiSourceVideoPage([property: JsonPropertyName("videos")] AiSourceVideoOption[] Videos);

internal static class AiVideoInputLimits
{
    public const long MaxSourceBytes = 32 * 1024 * 1024;
    public const long MaxImageTotalBytes = 20 * 1024 * 1024;
    public const long MaxAudioBytes = 15 * 1024 * 1024;

    public static string Kind(AiUploadSource source) => source.MediaType.Split(';')[0].Trim().ToLowerInvariant() switch
    {
        "image/png" or "image/jpeg" or "image/webp" => "image",
        "video/mp4" or "video/webm" => "video",
        "audio/wav" or "audio/x-wav" or "audio/mpeg" or "audio/mp3" => "audio",
        _ => throw new ArgumentException("Unsupported reference media type."),
    };

    public static void Validate(AiUploadSource source, string kind, long limit)
    {
        if (Kind(source) != kind || source.Length <= 0) throw new ArgumentException("Invalid input media.");
        if (source.Length > limit) throw new AiFileTooLargeException();
    }

    public static void ValidateReferences(IReadOnlyList<AiUploadSource> sources)
    {
        if (sources.Count > 13) throw new ArgumentException("Too many references.");
        foreach (var group in sources.GroupBy(Kind))
        {
            (int count, long perFile, long total) = group.Key switch
            {
                "image" => (9, AiRequestLimits.MaxFrameUploadBytes, MaxImageTotalBytes),
                "video" => (3, MaxSourceBytes, MaxSourceBytes),
                _ => (1, MaxAudioBytes, MaxAudioBytes),
            };
            if (group.Count() > count) throw new ArgumentException("Too many references of this kind.");
            foreach (var source in group) Validate(source, group.Key, perFile);
            if (group.Sum(source => source.Length) > total) throw new AiFileTooLargeException();
        }
        if (sources.Sum(source => source.Length) > MaxSourceBytes) throw new AiFileTooLargeException();
    }
}
