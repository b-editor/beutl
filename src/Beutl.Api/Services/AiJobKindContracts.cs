namespace Beutl.Api.Services;

public readonly struct AiJobOutcomeId : IEquatable<AiJobOutcomeId>
{
    private readonly string? _value;

    public AiJobOutcomeId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public bool Equals(AiJobOutcomeId other) => StringComparer.Ordinal.Equals(Value, other.Value);

    public override bool Equals(object? obj) => obj is AiJobOutcomeId other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(AiJobOutcomeId left, AiJobOutcomeId right) => left.Equals(right);

    public static bool operator !=(AiJobOutcomeId left, AiJobOutcomeId right) => !left.Equals(right);
}

public static class AiJobOutcomes
{
    public static AiJobOutcomeId Succeeded { get; } = new("succeeded");

    public static AiJobOutcomeId Failed { get; } = new("failed");

    public static AiJobOutcomeId Canceled { get; } = new("canceled");
}

public readonly record struct AiJobStatusSemantics
{
    public AiJobStatusSemantics(
        bool isTerminal,
        bool shouldPoll,
        AiJobOutcomeId? outcome = null)
    {
        if (isTerminal && shouldPoll)
            throw new ArgumentException("A terminal job cannot request polling.", nameof(shouldPoll));
        if (outcome.HasValue && outcome.Value.Value.Length == 0)
            throw new ArgumentException("A non-empty outcome identifier is required.", nameof(outcome));
        if (!isTerminal && outcome is not null)
            throw new ArgumentException("Only a terminal job can have an outcome.", nameof(outcome));

        IsTerminal = isTerminal;
        ShouldPoll = shouldPoll;
        Outcome = outcome;
    }

    public static AiJobStatusSemantics Unknown { get; } = new(false, false);

    public bool IsTerminal { get; }

    public bool ShouldPoll { get; }

    public AiJobOutcomeId? Outcome { get; }
}

public interface IAiJobStatusResolver
{
    AiJobStatusSemantics Resolve(AiJobStatusId status);
}

public sealed class AiJobStatusMap : IAiJobStatusResolver
{
    private readonly Dictionary<string, AiJobStatusSemantics> _statuses;

    public AiJobStatusMap(IEnumerable<KeyValuePair<AiJobStatusId, AiJobStatusSemantics>> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        _statuses = new Dictionary<string, AiJobStatusSemantics>(StringComparer.OrdinalIgnoreCase);
        foreach ((AiJobStatusId status, AiJobStatusSemantics semantics) in statuses)
        {
            if (!_statuses.TryAdd(status.Value, semantics))
                throw new ArgumentException($"Job status '{status}' is registered more than once.", nameof(statuses));
        }
    }

    public AiJobStatusSemantics Resolve(AiJobStatusId status)
    {
        return _statuses.TryGetValue(status.Value, out AiJobStatusSemantics semantics)
            ? semantics
            : AiJobStatusSemantics.Unknown;
    }
}

public interface IAiJobRefreshHandler
{
    Task RefreshAsync(AiJob job, CancellationToken cancellationToken);
}

/// <summary>
/// The resource-free result of checking whether a terminal AI job is eligible for retry.
/// </summary>
/// <remarks>
/// A preflight is only an estimate for presentation. It must not reserve
/// balance, create a durable idempotency key, or retain a lease. Call
/// <see cref="IAiJobRetryHandler.PrepareAsync"/> immediately before asking the
/// user to pay or dispatching a retry.
/// </remarks>
public sealed record AiJobRetryPreflight(
    bool IsAvailable,
    bool CanSubmit,
    string Explanation);

/// <summary>
/// Owns a retry prepared by a concrete handler. A preparation captures the
/// handler and durable request identity that produced it, so replacing an
/// extension after preparation cannot redirect the request to a different
/// implementation.
/// </summary>
/// <remarks>
/// <see cref="ExecuteAsync"/> is single-use. Dispose is idempotent; disposing
/// before execution abandons the unconsumed preparation, while disposing after
/// execution is a no-op because the handler owns the terminal/ambiguous outcome.
/// The host retains the originating job-kind descriptor lease until
/// <see cref="ExecuteAsync"/> completes, then always calls
/// <see cref="IAsyncDisposable.DisposeAsync"/>. An implementation that keeps
/// using extension-owned resources after cancellation must keep its returned
/// task incomplete until that work has drained; returning earlier permits the
/// extension to unload while its background work is still running.
/// </remarks>
public interface IAiJobRetryPreparation : IAsyncDisposable
{
    /// <summary>
    /// Executes the prepared retry exactly once. Task completion is the
    /// lifetime boundary after which the originating descriptor may unload.
    /// </summary>
    /// <param name="cancellationToken">
    /// Stops the host's request to wait. Implementations may finish already
    /// dispatched paid work, but must not complete this task until all use of
    /// extension-owned resources has ended.
    /// </param>
    Task ExecuteAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The outcome of preparing a retry. A blocked result has no owned resource;
/// a ready result owns exactly one preparation until it is taken or disposed.
/// </summary>
public sealed class AiJobRetryPreparationResult : IAsyncDisposable
{
    private readonly bool _isReady;
    private readonly string _explanation;
    private IAiJobRetryPreparation? _preparation;
    private int _disposed;

    private AiJobRetryPreparationResult(
        bool isReady,
        string explanation,
        IAiJobRetryPreparation? preparation)
    {
        _isReady = isReady;
        _explanation = explanation;
        _preparation = preparation;
    }

    public bool IsReady => _isReady;

    /// <summary>
    /// The user-facing reason when preparation is blocked. This is empty for a
    /// ready result.
    /// </summary>
    public string Explanation => _explanation;

    /// <summary>Creates a result that cannot be dispatched.</summary>
    public static AiJobRetryPreparationResult Blocked(string explanation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
        return new AiJobRetryPreparationResult(
            isReady: false,
            explanation,
            preparation: null);
    }

    /// <summary>Creates a result owning the supplied preparation.</summary>
    public static AiJobRetryPreparationResult Ready(IAiJobRetryPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        return new AiJobRetryPreparationResult(
            isReady: true,
            explanation: string.Empty,
            preparation);
    }

    /// <summary>
    /// Transfers ownership of the preparation to the caller. This succeeds
    /// once for a ready result and throws for blocked, disposed, or already
    /// transferred results.
    /// </summary>
    public IAiJobRetryPreparation TakePreparation()
    {
        if (!_isReady)
            throw new InvalidOperationException("A blocked retry has no preparation.");
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        return Interlocked.Exchange(ref _preparation, null)
            ?? throw new InvalidOperationException("The retry preparation was already taken.");
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        IAiJobRetryPreparation? preparation = Interlocked.Exchange(ref _preparation, null);
        if (preparation is not null)
            await preparation.DisposeAsync();
    }
}

/// <summary>
/// A prepared retry can no longer be executed because its durable identity,
/// account, or generation changed before dispatch.
/// </summary>
public sealed class AiJobRetryPreparationRejectedException : AiException
{
    public AiJobRetryPreparationRejectedException(Exception? innerException = null)
        : base(
            "The prepared AI retry is no longer valid. Start a new confirmation.",
            innerException)
    {
    }
}

/// <summary>
/// The retry store could not be read or updated while preparing a request.
/// </summary>
public sealed class AiJobRetryPreparationUnavailableException : AiException
{
    public AiJobRetryPreparationUnavailableException(Exception? innerException = null)
        : base(
            "The AI retry could not be prepared right now. Try again later.",
            innerException,
            isTransient: true)
    {
    }
}

public interface IAiJobRetryHandler
{
    /// <summary>Determines whether this handler supports retrying the job.</summary>
    bool CanRetry(AiJob job, AiJobStatusSemantics status);

    /// <summary>
    /// Returns a resource-free presentation estimate. This method must not
    /// allocate an idempotency key or retain request ownership.
    /// </summary>
    ValueTask<AiJobRetryPreflight> GetPreflightAsync(
        AiJob job,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revalidates the request immediately before dispatch and returns either
    /// a blocked reason or an owned, handler-bound preparation.
    /// </summary>
    /// <remarks>
    /// The caller disposes the result on every path. A ready result retains
    /// ownership until <see cref="AiJobRetryPreparationResult.TakePreparation"/>
    /// transfers it exactly once.
    /// </remarks>
    ValueTask<AiJobRetryPreparationResult> PrepareAsync(
        AiJob job,
        CancellationToken cancellationToken);
}

public sealed record AiJobKindDescriptor
{
    public AiJobKindDescriptor(
        AiJobKindId kind,
        IAiJobStatusResolver statusResolver)
    {
        if (kind.Value.Length == 0)
            throw new ArgumentException("A job kind identifier is required.", nameof(kind));

        Kind = kind;
        StatusResolver = statusResolver ?? throw new ArgumentNullException(nameof(statusResolver));
    }

    public AiJobKindId Kind { get; }

    public IAiJobStatusResolver StatusResolver { get; }

    public IAiJobRefreshHandler? RefreshHandler { get; init; }

    public IAiJobRetryHandler? RetryHandler { get; init; }

}

public enum AiJobKindRegistrationMode
{
    Add,
    Replace,
}
