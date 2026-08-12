using System.Collections.Immutable;
using Beutl.Api.Clients;

namespace Beutl.Api.Services;

public readonly struct AiOperationId : IEquatable<AiOperationId>
{
    private readonly string? _value;

    public AiOperationId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public bool Equals(AiOperationId other) => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is AiOperationId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AiOperationId left, AiOperationId right) => left.Equals(right);

    public static bool operator !=(AiOperationId left, AiOperationId right) => !left.Equals(right);
}

public static class AiOperations
{
    public static AiOperationId ImageGeneration { get; } = new("image.generate");

    public static AiOperationId VideoGeneration { get; } = new("video.generate");

    public static AiOperationId Transcription { get; } = new("audio.transcribe");

    public static AiOperationId CaptionTranslation { get; } = new("subtitle.translate");

    public static AiOperationId ImageEdit(AiImageEditTaskId task)
        => new($"image.edit.{task.Value}");
}

public readonly struct AiImageSizeId : IEquatable<AiImageSizeId>
{
    private readonly string? _value;

    public AiImageSizeId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public bool Equals(AiImageSizeId other) => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is AiImageSizeId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AiImageSizeId left, AiImageSizeId right) => left.Equals(right);

    public static bool operator !=(AiImageSizeId left, AiImageSizeId right) => !left.Equals(right);
}

public readonly struct AiImageEditTaskId : IEquatable<AiImageEditTaskId>
{
    private readonly string? _value;

    public AiImageEditTaskId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public bool Equals(AiImageEditTaskId other) => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is AiImageEditTaskId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AiImageEditTaskId left, AiImageEditTaskId right) => left.Equals(right);

    public static bool operator !=(AiImageEditTaskId left, AiImageEditTaskId right) => !left.Equals(right);
}

public readonly struct AiVideoResolutionId : IEquatable<AiVideoResolutionId>
{
    private readonly string? _value;

    public AiVideoResolutionId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public bool Equals(AiVideoResolutionId other) => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is AiVideoResolutionId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AiVideoResolutionId left, AiVideoResolutionId right) => left.Equals(right);

    public static bool operator !=(AiVideoResolutionId left, AiVideoResolutionId right) => !left.Equals(right);
}

public sealed record AiImageGenerationRequest
{
    public AiImageGenerationRequest(string prompt, AiImageSizeId size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        if (size.Value.Length == 0)
            throw new ArgumentException("An image size is required.", nameof(size));
        Prompt = prompt;
        Size = size;
    }

    public string Prompt { get; }

    public AiImageSizeId Size { get; }
}

public sealed record AiImageEditRequest
{
    public AiImageEditRequest(
        AiUploadSource image,
        AiImageEditTaskId task,
        string? prompt = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (task.Value.Length == 0)
            throw new ArgumentException("An image edit task is required.", nameof(task));
        Image = image;
        Task = task;
        Prompt = prompt;
    }

    public AiUploadSource Image { get; }

    public AiImageEditTaskId Task { get; }

    public string? Prompt { get; }
}

public sealed record AiTranscriptionRequest
{
    public AiTranscriptionRequest(AiUploadSource audio, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(audio);
        Audio = audio;
        Language = string.IsNullOrWhiteSpace(language) ? null : language.Trim();
    }

    public AiUploadSource Audio { get; }

    public string? Language { get; }
}

public sealed record AiCaptionTranslationRequest
{
    public AiCaptionTranslationRequest(
        IReadOnlyList<AiCaptionTranslationSegment> segments,
        string targetLanguage,
        string? sourceLanguage = null)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetLanguage);
        if (segments.Count == 0)
            throw new ArgumentException("At least one subtitle segment is required.", nameof(segments));
        if (segments.Any(segment => segment is null))
            throw new ArgumentException("Translation segments cannot contain null.", nameof(segments));

        Segments = Array.AsReadOnly(segments.ToArray());
        TargetLanguage = targetLanguage.Trim().ToLowerInvariant();
        SourceLanguage = string.IsNullOrWhiteSpace(sourceLanguage)
            ? null
            : sourceLanguage.Trim().ToLowerInvariant();
    }

    public IReadOnlyList<AiCaptionTranslationSegment> Segments { get; }

    public string TargetLanguage { get; }

    public string? SourceLanguage { get; }
}

public sealed record AiVideoGenerationRequest
{
    public AiVideoGenerationRequest(
        string prompt,
        int durationSeconds,
        AiVideoResolutionId resolution,
        AiUploadSource? firstFrame = null,
        AiUploadSource? lastFrame = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        if (durationSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (resolution.Value.Length == 0)
            throw new ArgumentException("A video resolution is required.", nameof(resolution));
        if (lastFrame is not null && firstFrame is null)
            throw new ArgumentException("A last frame requires a first frame.", nameof(lastFrame));

        Prompt = prompt;
        DurationSeconds = durationSeconds;
        Resolution = resolution;
        FirstFrame = firstFrame;
        LastFrame = lastFrame;
    }

    public string Prompt { get; }

    public int DurationSeconds { get; }

    public AiVideoResolutionId Resolution { get; }

    public AiUploadSource? FirstFrame { get; }

    public AiUploadSource? LastFrame { get; }
}

public sealed record AiJobPageRequest
{
    public AiJobPageRequest(string? cursor = null, int limit = 50)
    {
        if (limit is <= 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit));
        Cursor = string.IsNullOrWhiteSpace(cursor) ? null : cursor;
        Limit = limit;
    }

    public string? Cursor { get; }

    public int Limit { get; }
}

// Usage is expressed as a proportion. The server owns the unit accounting so the
// per-operation cost never reaches the client.
public sealed record AiMonthlyUsage(int UsedPercent, int RemainingPercent, bool IsExhausted);

public sealed record AiBalance(
    AiMonthlyUsage MonthlyUsage,
    int AdditionalCredits,
    bool HasAdditionalCreditDebt);

// Which operations the server will accept right now, keyed by operation id.
public sealed class AiOperationAvailability
{
    public AiOperationAvailability(IEnumerable<KeyValuePair<AiOperationId, bool>> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        Operations = operations.ToImmutableDictionary(pair => pair.Key, pair => pair.Value);
    }

    public ImmutableDictionary<AiOperationId, bool> Operations { get; }

    public bool CanStart(AiOperationId operation)
        => Operations.TryGetValue(operation, out bool allowed) && allowed;
}

public sealed record AiEntitlements(
    string? Plan,
    string? SubscriptionStatus,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    bool CanUseAi,
    AiBalance Balance,
    AiOperationAvailability Availability);

public sealed record AiImageResult(
    AiJobId? JobId,
    AiContentId FileId,
    Uri ContentUri);

public sealed record AiVideoGenerationResult(
    AiJobId JobId,
    AiJobStatusId Status);

public sealed record AiVideoJob(
    AiJobId JobId,
    AiJobStatusId Status,
    AiContentId? FileId,
    Uri? ContentUri,
    string? Error);

public sealed record AiTranscriptionResponse(
    AiJobId? JobId,
    AiTranscriptionSegment[] Segments,
    string? Language,
    AiTranscriptionWord[]? Words);

public sealed class AiTranscriptionWord
{
    public required double Start { get; init; }

    public required double End { get; init; }

    public required string Word { get; init; }
}

public sealed class AiTranscriptionSegment
{
    public required double Start { get; init; }

    public required double End { get; init; }

    public required string Text { get; init; }
}

public sealed record AiCaptionTranslationSegment
{
    public required string Id { get; init; }

    public required string Text { get; init; }

    public AiCaptionTranslationSegmentContext? Context { get; init; }
}

public sealed record AiCaptionTranslationSegmentContext
{
    public AiCaptionTranslationSegmentContext(
        string groupId,
        int partIndex,
        TimeSpan start,
        TimeSpan end)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupId);
        if (partIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(partIndex));
        if (start < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(start));
        if (end <= start)
            throw new ArgumentOutOfRangeException(nameof(end));

        GroupId = groupId.Trim();
        PartIndex = partIndex;
        Start = start;
        End = end;
    }

    public string GroupId { get; }

    public int PartIndex { get; }

    public TimeSpan Start { get; }

    public TimeSpan End { get; }
}

public sealed record AiCaptionTranslationResponse(
    AiJobId? JobId,
    AiCaptionTranslationSegment[] Segments);

public sealed record AiJobPage(
    ImmutableArray<AiJob> Jobs,
    string? NextCursor);

internal static class AiModelMapper
{
    public static AiEntitlements ToModel(EntitlementsResponse response)
    {
        if (!response.TryNormalize(out EntitlementsResponse? normalized))
            throw new AiException("The AI entitlement response is invalid.");
        EntitlementsResponse value = normalized!;

        return new AiEntitlements(
            value.Plan,
            value.SubscriptionStatus,
            ParseTimestamp(value.CurrentPeriodStart),
            ParseTimestamp(value.CurrentPeriodEnd),
            value.CancelAtPeriodEnd,
            value.CanUseAi,
            ToModel(value.Balance),
            new AiOperationAvailability(
                value.Availability.Select(pair =>
                    new KeyValuePair<AiOperationId, bool>(
                        new AiOperationId(pair.Key),
                        pair.Value))));
    }

    public static AiBalance ToModel(AiBalanceResponse response)
    {
        if (!response.TryNormalize(out AiBalanceResponse? normalized))
            throw new AiException("The AI balance response is invalid.");
        AiBalanceResponse value = normalized!;
        return new AiBalance(
            ToModel(value.MonthlyUsage),
            value.AdditionalCredits,
            value.HasAdditionalCreditDebt);
    }

    internal static AiMonthlyUsage ToModel(AiMonthlyUsageResponse response)
        => new(
            response.UsedPercent,
            response.RemainingPercent,
            response.IsExhausted);

    public static AiImageResult ToModel(AiImageResponse response)
        => new(
            ToOptionalJobId(response.JobId),
            new AiContentId(response.FileId),
            ParseContentUri(response.Url));

    public static AiVideoGenerationResult ToModel(CreateAiVideoResponse response)
        => new(
            new AiJobId(response.JobId),
            new AiJobStatusId(response.Status));

    public static AiVideoJob ToModel(AiVideoJobResponse response)
        => new(
            new AiJobId(response.JobId),
            new AiJobStatusId(response.Status),
            string.IsNullOrWhiteSpace(response.FileId) ? null : new AiContentId(response.FileId),
            string.IsNullOrWhiteSpace(response.Url) ? null : ParseContentUri(response.Url),
            response.Error);

    public static AiTranscriptionResponse ToModel(AiTranscriptionResponseDto response)
        => new(
            ToOptionalJobId(response.JobId),
            response.Segments.Select(segment => new AiTranscriptionSegment
            {
                Start = segment.Start,
                End = segment.End,
                Text = segment.Text,
            }).ToArray(),
            response.Language,
            response.Words?.Select(word => new AiTranscriptionWord
            {
                Start = word.Start,
                End = word.End,
                Word = word.Word,
            }).ToArray());

    public static AiCaptionTranslationResponse ToModel(AiCaptionTranslationResponseDto response)
        => new(
            ToOptionalJobId(response.JobId),
            response.Segments.Select(segment => new AiCaptionTranslationSegment
            {
                Id = segment.Id,
                Text = segment.Text,
                Context = segment.Context is null
                    ? null
                    : new AiCaptionTranslationSegmentContext(
                        segment.Context.GroupId,
                        segment.Context.PartIndex,
                        TimeSpan.FromSeconds(segment.Context.Start),
                        TimeSpan.FromSeconds(segment.Context.End)),
            }).ToArray());

    public static AiJob ToModel(AiJobHistoryResponse response)
        => new(
            new AiJobId(response.Id),
            new AiJobKindId(response.Kind),
            new AiJobStatusId(response.Status),
            response.InputParams?.Clone(),
            string.IsNullOrWhiteSpace(response.FileId) ? null : new AiContentId(response.FileId),
            string.IsNullOrWhiteSpace(response.Url) ? null : ParseContentUri(response.Url),
            response.Error,
            response.CanRetry,
            response.CreatedAt.ToUniversalTime(),
            response.UpdatedAt.ToUniversalTime());

    private static AiJobId? ToOptionalJobId(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new AiJobId(value);

    private static Uri ParseContentUri(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            ? uri
            : throw new AiException("The AI response contains an invalid content URI.");

    private static DateTimeOffset? ParseTimestamp(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : DateTimeOffset.TryParse(value, out DateTimeOffset timestamp)
                ? timestamp.ToUniversalTime()
                : throw new AiException("The AI entitlement response contains an invalid timestamp.");
}
