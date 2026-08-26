using Beutl.Media;

namespace Beutl.Engine;

public interface IPresenter
{
    Type TargetType { get; }
}

public interface IPresenter<T> : IPresenter
    where T : CoreObject
{
    IProperty<T?> Target { get; }

    Type IPresenter.TargetType => typeof(T);
}

/// <summary>
/// Provides the time mapping for a presenter whose target is evaluated on a different
/// timeline. Presenters that only forward their target can continue to implement
/// <see cref="IPresenter{T}"/> without this contract.
/// </summary>
public interface ITimeMappingPresenter<T> : IPresenter<T>
    where T : CoreObject
{
    /// <summary>
    /// Maps a presenter-time interval to the target-time interval it evaluates.
    /// </summary>
    TimeRange CalculateTargetTimeRange(TimeRange timeRange, T target);

    /// <summary>
    /// Reports whether the mapped interval has no finite tail bound for the operation being
    /// evaluated. Looping presenters should return <see langword="true"/> when the target can
    /// continue to provide valid content indefinitely after <paramref name="timeRange"/>.
    /// </summary>
    bool HasUnboundedTail(TimeRange timeRange, T target);

    /// <summary>
    /// Calculates the presenter time needed to consume a target-time duration beginning at
    /// <paramref name="start"/>. <paramref name="reverse"/> describes the traversal direction
    /// inherited from an outer presenter. <paramref name="targetDuration"/> may be
    /// <see cref="TimeSpan.MaxValue"/> to represent an unbounded duration; implementations
    /// must propagate that sentinel without performing arithmetic on it.
    /// </summary>
    TimeSpan CalculateTimelineDuration(
        TimeSpan start,
        TimeSpan targetDuration,
        T target,
        bool reverse = false);

    /// <summary>
    /// Gets whether this presenter reverses the target timeline.
    /// </summary>
    bool IsReversed { get; }
}
