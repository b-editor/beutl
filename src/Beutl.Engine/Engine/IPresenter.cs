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
/// Describes one half-open composition-time interval during which a presenter resolves the
/// same target object by reference identity. A <see langword="null"/> target explicitly
/// represents an interval with no presented object.
/// </summary>
/// <param name="CompositionRange">The half-open composition-time interval.</param>
/// <param name="Target">The target resolved throughout the interval, or <see langword="null"/>.</param>
public readonly record struct PresenterTargetState(TimeRange CompositionRange, CoreObject? Target);

/// <summary>
/// Exposes a presenter's complete target-state partition without requiring callers to know the
/// target type at compile time.
/// </summary>
public interface ITargetStatePresenter : IPresenter
{
    /// <summary>
    /// Tries to describe every target state in <paramref name="compositionRange"/>. On success,
    /// <paramref name="states"/> must be a sorted, non-overlapping partition of the requested
    /// range using half-open intervals. The requested range may contain negative composition
    /// time. Implementations must return <see langword="false"/> when they cannot prove that the
    /// partition is complete, such as for an arbitrary expression.
    /// Every non-null target must be assignable to <see cref="IPresenter.TargetType"/>.
    /// </summary>
    bool TryGetTargetStates(
        TimeRange compositionRange,
        out IReadOnlyList<PresenterTargetState> states);
}

/// <summary>
/// Provides the default target-state contract for a typed presenter. Presenters with dynamic
/// targets can implement <see cref="ITargetStatePresenter.TryGetTargetStates"/> directly when
/// they can describe the target changes exactly.
/// </summary>
public interface ITargetStatePresenter<T> : IPresenter<T>, ITargetStatePresenter
    where T : CoreObject
{
    bool ITargetStatePresenter.TryGetTargetStates(
        TimeRange compositionRange,
        out IReadOnlyList<PresenterTargetState> states)
    {
        if (compositionRange.IsEmpty)
        {
            states = [];
            return true;
        }

        if (Target.HasExpression || Target.Animation != null)
        {
            states = [];
            return false;
        }

        states = [new PresenterTargetState(compositionRange, Target.CurrentValue)];
        return true;
    }
}

/// <summary>
/// Provides time mapping through a non-generic presenter boundary so callers can traverse
/// presenters without knowing the target type at compile time.
/// </summary>
public interface ITimeMappingPresenter : ITargetStatePresenter
{
    /// <summary>
    /// Gets whether the presenter can completely describe its time mapping over the requested
    /// composition-time interval. Implementations must return <see langword="false"/> when a
    /// mapping property cannot be evaluated conservatively, such as for an arbitrary expression.
    /// </summary>
    bool CanProvideCompleteTimeMapping(
        TimeRange timeRange,
        CoreObject target,
        bool reverse = false);

    /// <summary>
    /// Maps a presenter-time interval to the target-time interval it evaluates.
    /// </summary>
    TimeRange CalculateTargetTimeRange(TimeRange timeRange, CoreObject target);

    /// <summary>
    /// Reports whether the mapped interval has no finite tail bound for the operation being
    /// evaluated.
    /// </summary>
    bool HasUnboundedTail(TimeRange timeRange, CoreObject target, bool reverse = false);

    /// <summary>
    /// Calculates the presenter time needed to consume a target-time duration beginning at
    /// <paramref name="start"/>.
    /// </summary>
    TimeSpan CalculateTimelineDuration(
        TimeSpan start,
        TimeSpan targetDuration,
        CoreObject target,
        bool reverse = false);

    /// <summary>
    /// Gets whether this presenter reverses the target timeline over the requested interval.
    /// </summary>
    bool IsReversed(TimeRange timeRange, CoreObject target);
}

/// <summary>
/// Provides the time mapping for a presenter whose target is evaluated on a different
/// timeline. Presenters that only forward their target can continue to implement
/// <see cref="ITargetStatePresenter{T}"/> without this contract.
/// </summary>
public interface ITimeMappingPresenter<T> : ITargetStatePresenter<T>, ITimeMappingPresenter
    where T : CoreObject
{
    bool ITimeMappingPresenter.CanProvideCompleteTimeMapping(
        TimeRange timeRange,
        CoreObject target,
        bool reverse)
        => CanProvideCompleteTimeMapping(timeRange, (T)target, reverse);

    TimeRange ITimeMappingPresenter.CalculateTargetTimeRange(TimeRange timeRange, CoreObject target)
        => CalculateTargetTimeRange(timeRange, (T)target);

    bool ITimeMappingPresenter.HasUnboundedTail(
        TimeRange timeRange,
        CoreObject target,
        bool reverse)
        => HasUnboundedTail(timeRange, (T)target, reverse);

    TimeSpan ITimeMappingPresenter.CalculateTimelineDuration(
        TimeSpan start,
        TimeSpan targetDuration,
        CoreObject target,
        bool reverse)
        => CalculateTimelineDuration(start, targetDuration, (T)target, reverse);

    bool ITimeMappingPresenter.IsReversed(TimeRange timeRange, CoreObject target)
        => IsReversed(timeRange, (T)target);

    /// <summary>
    /// Gets whether the presenter can completely describe its time mapping over the requested
    /// composition-time interval.
    /// </summary>
    bool CanProvideCompleteTimeMapping(
        TimeRange timeRange,
        T target,
        bool reverse = false);

    /// <summary>
    /// Maps a presenter-time interval to the target-time interval it evaluates.
    /// </summary>
    TimeRange CalculateTargetTimeRange(TimeRange timeRange, T target);

    /// <summary>
    /// Reports whether the mapped interval has no finite tail bound for the operation being
    /// evaluated. Looping presenters should return <see langword="true"/> when the target can
    /// continue to provide valid content indefinitely after <paramref name="timeRange"/>.
    /// </summary>
    bool HasUnboundedTail(TimeRange timeRange, T target, bool reverse = false);

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
    /// Gets whether this presenter reverses the target timeline over the requested interval.
    /// </summary>
    bool IsReversed(TimeRange timeRange, T target);
}
