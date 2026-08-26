using Beutl.Composition;
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
/// Provides time mapping through a non-generic presenter boundary so callers can traverse
/// presenters without knowing the target type at compile time.
/// </summary>
public interface ITimeMappingPresenter : IPresenter
{
    /// <summary>
    /// Gets the target property evaluated by this presenter.
    /// </summary>
    IProperty TargetProperty { get; }

    /// <summary>
    /// Resolves the target using the supplied composition time.
    /// </summary>
    CoreObject? GetTarget(CompositionContext context);

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
/// <see cref="IPresenter{T}"/> without this contract.
/// </summary>
public interface ITimeMappingPresenter<T> : IPresenter<T>, ITimeMappingPresenter
    where T : CoreObject
{
    IProperty ITimeMappingPresenter.TargetProperty => Target;

    CoreObject? ITimeMappingPresenter.GetTarget(CompositionContext context)
        => Target.GetValue(context);

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
