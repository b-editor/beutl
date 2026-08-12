namespace Beutl.Services;

/// <summary>
/// Aggregates high-frequency product operations into a bounded fixed-period summary.
/// The timer uses <see cref="TimeProvider"/> so the period is deterministic in tests.
/// </summary>
internal sealed class ProductSummaryBuffer : IDisposable
{
    internal const int MaximumDistinctFeatures = 256;
    internal const string OverflowFeatureId = "overflow";

    private readonly object _gate = new();
    private readonly Dictionary<ProductSummaryKey, ProductSummaryBucket> _buckets = [];
    private readonly HashSet<string> _featureIds = new(StringComparer.Ordinal);
    private readonly ITimer _timer;

    internal ProductSummaryBuffer(TimeProvider timeProvider, TimeSpan interval, Action onIntervalElapsed)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(onIntervalElapsed);
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval));
        }

        _timer = timeProvider.CreateTimer(
            static state => ((Action)state!).Invoke(),
            onIntervalElapsed,
            interval,
            interval);
    }

    internal void Add(ProductSummaryKey key, string outcome, string? errorCode, double durationMilliseconds)
    {
        lock (_gate)
        {
            ProductSummaryKey boundedKey = key with { FeatureId = GetBoundedFeatureId(key.FeatureId) };
            if (!_buckets.TryGetValue(boundedKey, out ProductSummaryBucket? bucket))
            {
                bucket = new ProductSummaryBucket();
                _buckets.Add(boundedKey, bucket);
            }

            bucket.Add(outcome, errorCode, durationMilliseconds);
        }
    }

    internal ProductSummarySnapshot[] Drain()
    {
        lock (_gate)
        {
            ProductSummarySnapshot[] snapshots = _buckets
                .Select(static pair => pair.Value.ToSnapshot(pair.Key))
                .Where(static snapshot => snapshot.Count > 0)
                .ToArray();
            _buckets.Clear();
            _featureIds.Clear();
            return snapshots;
        }
    }

    internal void Clear()
    {
        lock (_gate)
        {
            _buckets.Clear();
            _featureIds.Clear();
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
    }

    private string? GetBoundedFeatureId(string? featureId)
    {
        if (featureId is null || featureId is "generic" or OverflowFeatureId)
        {
            return featureId;
        }

        if (_featureIds.Contains(featureId))
        {
            return featureId;
        }

        if (_featureIds.Count < MaximumDistinctFeatures)
        {
            _featureIds.Add(featureId);
            return featureId;
        }

        return OverflowFeatureId;
    }
}

internal readonly record struct ProductSummaryKey(string Name, string Trigger, string? FeatureId);

internal sealed class ProductSummaryBucket
{
    private readonly Dictionary<string, int> _outcomes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _errorCodes = new(StringComparer.Ordinal);
    private int _count;
    private double _durationMilliseconds;

    internal void Add(string outcome, string? errorCode, double durationMilliseconds)
    {
        _count++;
        _durationMilliseconds += double.IsFinite(durationMilliseconds) && durationMilliseconds >= 0
            ? durationMilliseconds
            : 0;
        _outcomes[outcome] = _outcomes.GetValueOrDefault(outcome) + 1;
        if (errorCode is not null)
        {
            _errorCodes.Add(errorCode);
        }
    }

    internal ProductSummarySnapshot ToSnapshot(ProductSummaryKey key)
    {
        string outcome = GetOutcome();
        string? errorCode = _errorCodes.Count == 1 ? _errorCodes.Single() : null;
        double averageDuration = _count == 0 ? 0 : _durationMilliseconds / _count;
        return new ProductSummarySnapshot(key, _count, outcome, errorCode, averageDuration);
    }

    private string GetOutcome()
    {
        if (_outcomes.Count == 1)
        {
            return _outcomes.Keys.Single();
        }

        if (_outcomes.ContainsKey(ProductOutcomes.Failed))
        {
            return ProductOutcomes.Failed;
        }

        return ProductOutcomes.Partial;
    }
}

internal readonly record struct ProductSummarySnapshot(
    ProductSummaryKey Key,
    int Count,
    string Outcome,
    string? ErrorCode,
    double AverageDurationMilliseconds);
