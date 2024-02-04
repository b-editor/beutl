using Beutl.Animation;

namespace Beutl.Rendering;

internal sealed class ZeroClock : IClock
{
    public static readonly ZeroClock Instance = new();

    public TimeSpan CurrentTime => TimeSpan.Zero;

    public TimeSpan AudioStartTime => TimeSpan.Zero;

    public TimeSpan BeginTime => TimeSpan.Zero;

    public TimeSpan DurationTime => TimeSpan.Zero;

    public IClock GlobalClock => this;
}

public sealed class InstanceClock : IClock
{
    public InstanceClock()
    {
        GlobalClock = this;
    }

    public TimeSpan CurrentTime { get; set; }

    public TimeSpan AudioStartTime { get; set; }

    public TimeSpan BeginTime { get; set; }

    public TimeSpan DurationTime { get; set; }

    public IClock GlobalClock { get; set; }
}
