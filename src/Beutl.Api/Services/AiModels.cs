using System.Collections.Immutable;

namespace Beutl.Api.Services;

// Usage is expressed as a proportion. The server owns the unit accounting so the
// per-operation cost never reaches the client.
public sealed record AiMonthlyUsage(int UsedPercent, int RemainingPercent, bool IsExhausted);

public sealed record AiBalance(
    AiMonthlyUsage MonthlyUsage,
    int AdditionalCredits,
    bool HasAdditionalCreditDebt);

/// <summary>
/// What the server has said about starting an operation. "Not answered" is a
/// state of its own: a server that never mentioned an operation has not refused
/// it, and reporting that as a refusal sends the account to buy credits it
/// already has.
/// </summary>
public enum AiOperationAvailabilityState
{
    Unknown,
    Available,
    Unavailable,
}

// Which operations the server will accept right now, keyed by operation id.
public sealed class AiOperationAvailability
{
    public AiOperationAvailability(IEnumerable<KeyValuePair<AiOperationId, bool>> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        Operations = operations.ToImmutableDictionary(pair => pair.Key, pair => pair.Value);
    }

    public ImmutableDictionary<AiOperationId, bool> Operations { get; }

    /// <summary>
    /// An operation the server did not report reads as
    /// <see cref="AiOperationAvailabilityState.Unknown"/>, mirroring
    /// <see cref="AiModelAvailability.CanStart"/>: silence is not a refusal.
    /// </summary>
    public AiOperationAvailabilityState GetState(AiOperationId operation)
    {
        if (!Operations.TryGetValue(operation, out bool allowed))
            return AiOperationAvailabilityState.Unknown;
        return allowed
            ? AiOperationAvailabilityState.Available
            : AiOperationAvailabilityState.Unavailable;
    }
}

/// <summary>
/// Which of an operation's models the account can pay for right now. An
/// operation reads as available when any one of them does, so a picker needs
/// this to know which entries to offer.
/// </summary>
public sealed class AiModelAvailability
{
    public static AiModelAvailability Empty { get; } =
        new(ImmutableDictionary<AiOperationId, ImmutableDictionary<AiModelId, bool>>.Empty);

    public AiModelAvailability(
        IEnumerable<KeyValuePair<AiOperationId, ImmutableDictionary<AiModelId, bool>>> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        Operations = operations.ToImmutableDictionary(pair => pair.Key, pair => pair.Value);
    }

    public ImmutableDictionary<AiOperationId, ImmutableDictionary<AiModelId, bool>> Operations { get; }

    /// <summary>
    /// Whether that model can be started. An operation the server did not
    /// report says nothing about its models, so the answer falls back to the
    /// operation-wide flag its caller already has.
    /// </summary>
    public bool CanStart(AiOperationId operation, AiModelId model, bool fallback)
        => Operations.TryGetValue(operation, out ImmutableDictionary<AiModelId, bool>? models)
           && models.TryGetValue(model, out bool allowed)
            ? allowed
            : fallback;
}

public sealed record AiEntitlements(
    string? Plan,
    string? SubscriptionStatus,
    DateTimeOffset? CurrentPeriodStart,
    DateTimeOffset? CurrentPeriodEnd,
    bool CancelAtPeriodEnd,
    bool CanUseAi,
    AiBalance Balance,
    AiOperationAvailability Availability)
{
    /// <summary>
    /// Empty against a server that predates per-model pricing, which is the
    /// same as saying nothing about any particular model.
    /// </summary>
    public AiModelAvailability ModelAvailability { get; init; } = AiModelAvailability.Empty;
}

public sealed record AiImageResult(
    AiJobId? JobId,
    AiContentId FileId,
    Uri ContentUri,
    AiContentMetadata? ContentMetadata = null);

public sealed record AiVideoGenerationResult(
    AiJobId JobId,
    AiJobStatusId Status);

public sealed record AiVideoJob(
    AiJobId JobId,
    AiJobStatusId Status,
    AiContentId? FileId,
    Uri? ContentUri,
    string? Error,
    AiContentMetadata? ContentMetadata = null);

public sealed record AiContentDownload(AiContentMetadata? Metadata);

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
        if (!AiRequestLimits.IsSafeTranslationIdentifier(groupId))
            throw new ArgumentException(
                "Translation context group IDs must be 1 to 64 safe ASCII characters.",
                nameof(groupId));
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

/// <summary>
/// A rough version of a picture, sent while the finished one is still being
/// worked out. The bytes are a whole image of their own and can be shown as
/// they are.
/// </summary>
public sealed record AiImagePreview(int Index, ReadOnlyMemory<byte> Bytes);

public sealed record AiCaptionTranslationResponse(
    AiJobId? JobId,
    AiCaptionTranslationSegment[] Segments);

public sealed record AiJobPage(
    ImmutableArray<AiJob> Jobs,
    string? NextCursor);
