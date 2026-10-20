namespace Beutl.Api.Services;

public readonly record struct AiJobOutcomeId
{
    private readonly string? _value;

    public AiJobOutcomeId(string value) => _value = AiIdentifier.Normalize(value, nameof(value));

    public string Value => _value ?? string.Empty;

    public override string ToString() => Value;
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
