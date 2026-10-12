using System.Text.Json;

namespace Beutl.Api.Services;

public readonly record struct AiJobId
{
    private readonly string? _value;

    public AiJobId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public override string ToString() => Value;
}

public readonly record struct AiContentId
{
    private readonly string? _value;

    public AiContentId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public override string ToString() => Value;
}

public readonly record struct AiJobKindId
{
    private readonly string? _value;

    public AiJobKindId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public override string ToString() => Value;
}

public static class AiJobKinds
{
    public static AiJobKindId Image { get; } = new("image");

    public static AiJobKindId ImageEdit { get; } = new("image_edit");

    public static AiJobKindId Transcription { get; } = new("stt");

    public static AiJobKindId CaptionTranslation { get; } = new("translation");

    public static AiJobKindId Video { get; } = new("video");
}

public readonly record struct AiJobStatusId
{
    private readonly string? _value;

    public AiJobStatusId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public override string ToString() => Value;
}

public static class AiJobStatuses
{
    public static AiJobStatusId Queued { get; } = new("queued");

    public static AiJobStatusId Running { get; } = new("running");

    public static AiJobStatusId Finalizing { get; } = new("finalizing");

    public static AiJobStatusId Succeeded { get; } = new("succeeded");

    public static AiJobStatusId Failed { get; } = new("failed");

    public static AiJobStatusId Canceled { get; } = new("canceled");
}

public sealed record AiJob(
    AiJobId Id,
    AiJobKindId Kind,
    AiJobStatusId Status,
    JsonElement? InputParameters,
    AiContentId? FileId,
    Uri? ContentUri,
    string? Error,
    bool CanRetry,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    AiContentMetadata? ContentMetadata = null)
{
    /// <summary>
    /// The model the job ran on, or null for one created before an operation
    /// could offer more than one. A rerun repeats it rather than falling back
    /// to whatever the default is now.
    /// </summary>
    public AiModelId? Model { get; init; }
}

internal static class AiIdentifier
{
    public static string Normalize(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        string normalized = value.Trim();
        if (normalized.Length > 256)
            throw new ArgumentException("The identifier is too long.", parameterName);
        return normalized;
    }
}
