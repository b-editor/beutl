using Beutl.Animation;

namespace Beutl.Rendering;

internal sealed class ZeroClock : IClock
{
    public static readonly ZeroClock Instance = new();

    public TimeSpan CurrentTime => TimeSpan.Zero;
}

internal sealed class InstanceClock : IClock
{
    public TimeSpan CurrentTime { get; set; }
}
