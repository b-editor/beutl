using System.Text.Json;

namespace Beutl.Api.Services;

public readonly struct AiJobId : IEquatable<AiJobId>
{
    private readonly string? _value;

    public AiJobId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public bool Equals(AiJobId other) => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is AiJobId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AiJobId left, AiJobId right) => left.Equals(right);

    public static bool operator !=(AiJobId left, AiJobId right) => !left.Equals(right);
}

public readonly struct AiContentId : IEquatable<AiContentId>
{
    private readonly string? _value;

    public AiContentId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public bool Equals(AiContentId other) => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is AiContentId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AiContentId left, AiContentId right) => left.Equals(right);

    public static bool operator !=(AiContentId left, AiContentId right) => !left.Equals(right);
}

public readonly struct AiJobKindId : IEquatable<AiJobKindId>
{
    private readonly string? _value;

    public AiJobKindId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public bool Equals(AiJobKindId other) => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is AiJobKindId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AiJobKindId left, AiJobKindId right) => left.Equals(right);

    public static bool operator !=(AiJobKindId left, AiJobKindId right) => !left.Equals(right);
}

public static class AiJobKinds
{
    public static AiJobKindId Image { get; } = new("image");

    public static AiJobKindId ImageEdit { get; } = new("image_edit");

    public static AiJobKindId Transcription { get; } = new("stt");

    public static AiJobKindId CaptionTranslation { get; } = new("translation");

    public static AiJobKindId Video { get; } = new("video");
}

public readonly struct AiJobStatusId : IEquatable<AiJobStatusId>
{
    private readonly string? _value;

    public AiJobStatusId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public bool Equals(AiJobStatusId other) => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is AiJobStatusId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AiJobStatusId left, AiJobStatusId right) => left.Equals(right);

    public static bool operator !=(AiJobStatusId left, AiJobStatusId right) => !left.Equals(right);
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
    AiContentMetadata? ContentMetadata = null);

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
