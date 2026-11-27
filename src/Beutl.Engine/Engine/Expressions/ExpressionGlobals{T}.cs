using Beutl.Animation;

namespace Beutl.Engine.Expressions;

public class ExpressionGlobals<T>(ExpressionContext context, IExpression<T>? below) : ExpressionGlobals(context)
{
    public T Value => ValueAt(Context.Time);

    public T ValueAtTime(double time) => ValueAt(MediaTime.FromSeconds(Start + time));

    /// <summary>Offsets each numeric component of <see cref="Value"/> with smooth noise sampled at the owner's local time.</summary>
    /// <param name="frequency">The number of wiggles per second.</param>
    /// <param name="amplitude">The scale of each offset, measured in the component's units.</param>
    /// <param name="octaves">The number of noise layers, up to 16.</param>
    /// <param name="seed">An optional seed. The property's identity supplies the default.</param>
    /// <returns>The value with independent offsets, or <see cref="Value"/> when amplitude is zero or octaves is non-positive.</returns>
    /// <remarks>Integer components round to nearest, ties to even, and clamp to their range.
    /// Color offsets use stored sRGB byte units and preserve alpha.</remarks>
    /// <exception cref="ExpressionException">The value type is unsupported, or an offset cannot produce a finite value.</exception>
    public T WiggleValue(double frequency, double amplitude, int octaves = 1, int? seed = null)
    {
        var wiggle = ComponentAnimations.GetWiggle<T>();
        if (!double.IsFinite(frequency) || !double.IsFinite(amplitude))
            return wiggle(Value, double.NaN, amplitude, octaves, 0);
        if (amplitude == 0 || octaves <= 0)
            return Value;

        return wiggle(Value, Time * frequency, amplitude, octaves, GetWiggleSeed(seed));
    }

    /// <summary>Extends the underlying animation after its last keyframe.</summary>
    /// <param name="mode">How to extend the animation.</param>
    /// <param name="keyframes">The number of distinct keyframe times to use from the end. Zero uses all keyframes.</param>
    /// <returns>The extended value, or <see cref="Value"/> up to the last keyframe or with fewer than two times.</returns>
    /// <exception cref="ExpressionException">The count is negative, or the active mode does not support the value type.</exception>
    public T LoopOut(LoopMode mode = LoopMode.Cycle, int keyframes = 0) => Loop(mode, keyframes, loopOut: true);

    /// <summary>Extends the underlying animation before its first keyframe.</summary>
    /// <param name="mode">How to extend the animation.</param>
    /// <param name="keyframes">The number of distinct keyframe times to use from the start. Zero uses all keyframes.</param>
    /// <returns>The extended value, or <see cref="Value"/> from the first keyframe onward or with fewer than two times.</returns>
    /// <exception cref="ExpressionException">The count is negative, or the active mode does not support the value type.</exception>
    public T LoopIn(LoopMode mode = LoopMode.Cycle, int keyframes = 0) => Loop(mode, keyframes, loopOut: false);

    private T Loop(LoopMode mode, int keyframes, bool loopOut)
    {
        if (keyframes < 0)
            throw new ExpressionException("The number of keyframes to loop must be non-negative.");
        // Report an unsupported type at once, not only after the playhead leaves the keyframe range.
        if (mode is LoopMode.Offset or LoopMode.Continue)
            ComponentAnimations.EnsureScaledDeltaSupported<T>();
        if (keyframes == 1)
            return Value;

        MediaTime[] times = GetLoopKeyTimes();
        if (times.Length < 2)
            return Value;

        int count = keyframes == 0 ? times.Length : Math.Min(keyframes, times.Length);
        int first = loopOut ? times.Length - count : 0;
        int last = first + count - 1;
        MediaTime start = times[first];
        MediaTime end = times[last];
        MediaTime time = Context.Time;
        if (loopOut ? time <= end : time >= start)
            return Value;

        if (mode == LoopMode.Continue)
            return ContinueLoop(time, loopOut ? end : start, loopOut);

        // The distinct scene times give a positive duration. Widen differences between signed flick positions.
        Int128 duration = (Int128)end.Flicks - start.Flicks;
        (Int128 cycles, Int128 phase) = Int128.DivRem((Int128)time.Flicks - start.Flicks, duration);
        if (phase < 0)
        {
            phase += duration;
            cycles--;
        }

        MediaTime wrapped = new((long)(start.Flicks + phase));
        return mode switch
        {
            LoopMode.Cycle => ValueAt(wrapped),
            LoopMode.PingPong => ValueAt(Int128.IsOddInteger(cycles) ? new MediaTime((long)(end.Flicks - phase)) : wrapped),
            // Each cycle carries the range's full displacement, including a jump between keys sharing its outer
            // boundary time: a continuation that has not yet moved gives the value held just outside the range,
            // read from the keys rather than by evaluating anything outside it.
            LoopMode.Offset => loopOut
                ? ComponentAnimations.AddScaledDelta(ValueAt(wrapped), ValueAt(start), ContinueLoop(end, end, true), (double)cycles)
                : ComponentAnimations.AddScaledDelta(ValueAt(wrapped), ContinueLoop(start, start, false), ValueAt(end), (double)cycles),
            _ => throw new ExpressionException($"Unknown loop mode: {mode}.")
        };
    }

    private T ContinueLoop(MediaTime time, MediaTime boundary, bool loopOut) => below switch
    {
        KeyFrameAnimation<T> animation => KeyFrameContinuation.Continue(animation, ValueAt(boundary), time, boundary, loopOut),
        ILoopContinuation<T> source => source.Continue(Context.WithTime(boundary), time, loopOut),
        _ => ValueAt(boundary)
    };

    private MediaTime[] GetLoopKeyTimes()
    {
        IEnumerable<MediaTime> times = below switch
        {
            IKeyFrameAnimation animation => GetSceneKeyTimes(animation),
            IComponentAnimation animation => GetComponentKeyTimes(animation.Components),
            ICombinedValueSource source => GetComponentKeyTimes(source.Components),
            _ => []
        };
        return times.Distinct().Order().ToArray();
    }

    private static IEnumerable<MediaTime> GetComponentKeyTimes(IReadOnlyList<IProperty> components)
        => components.Select(component => component.GetKeyFrameAnimation()).OfType<IKeyFrameAnimation>().SelectMany(GetSceneKeyTimes);

    private static IEnumerable<MediaTime> GetSceneKeyTimes(IKeyFrameAnimation animation)
        => animation.KeyFrames.Select(key => animation.ToScene(key.KeyTime.Value));

    private T ValueAt(MediaTime sceneTime)
    {
        // Keyframes without a key give the animator's default, not a value; the stored value stands then.
        if (below is not null and not IKeyFrameAnimation { KeyFrames.Count: 0 })
        {
            T value = below.Evaluate(Context.WithTime(sceneTime));
            if (value is not null)
                return value;
        }

        if (Context.CurrentProperty is IProperty<T> property)
            return property.Value;

        throw new ExpressionException("Value is not available in this expression.");
    }
}
