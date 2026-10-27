namespace Beutl.Graphics.Shaders;

/// <summary>One recorded simulation update. Time is the end of the step; Delta is positive.</summary>
public readonly record struct ShaderTimeStep(int Index, TimeSpan Time, TimeSpan Delta);

public sealed partial class ShaderGraphBuilder
{
    /// <summary>Records a finite sequence of updates. Each callback runs now and returns the next virtual state.</summary>
    /// <remarks>Use the index in resource and pass names. Callbacks and mutable author state are never retained.</remarks>
    public T Repeat<T>(int count, T initial, Func<ShaderGraphBuilder, T, int, T> record)
    {
        EnsureRecording();
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentNullException.ThrowIfNull(record);
        T state = initial;
        for (int index = 0; index < count; index++)
            state = record(this, state, index);
        return state;
    }

    /// <summary>Records deterministic history from an initialized state to a requested time.</summary>
    /// <remarks>
    /// Every recording initializes again at resetTime, then records fixed steps and, if needed, a shorter
    /// final step. At or before resetTime only initialization runs. Seeking and repeated renders therefore
    /// never consume a previously previewed frame. Change resetTime or initialization data to reset history.
    /// Source is the current request's image, not a past video frame; record time-dependent inputs in each
    /// update when the simulation needs them. The callbacks run only during recording.
    /// </remarks>
    public T Replay<T>(TimeSpan time, TimeSpan step, Func<ShaderGraphBuilder, T> initialize,
        Func<ShaderGraphBuilder, T, ShaderTimeStep, T> update, TimeSpan resetTime = default)
    {
        EnsureRecording();
        ArgumentNullException.ThrowIfNull(initialize);
        ArgumentNullException.ThrowIfNull(update);
        if (step.Ticks <= 0) throw new ArgumentOutOfRangeException(nameof(step));
        long ticks = time > resetTime ? checked(time.Ticks - resetTime.Ticks) : 0;
        long count = ticks / step.Ticks + (ticks % step.Ticks == 0 ? 0 : 1);
        if (count > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(time));
        T state = initialize(this);
        long elapsed = 0;
        for (int index = 0; index < count; index++)
        {
            long delta = Math.Min(step.Ticks, ticks - elapsed);
            elapsed += delta;
            state = update(this, state, new(index, TimeSpan.FromTicks(checked(resetTime.Ticks + elapsed)), TimeSpan.FromTicks(delta)));
        }
        return state;
    }
}
