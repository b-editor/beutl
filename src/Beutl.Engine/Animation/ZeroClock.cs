namespace Beutl.Animation;

internal sealed class ZeroClock : IClock
{
    public static readonly ZeroClock Instance = new();

    public TimeSpan CurrentTime => TimeSpan.Zero;

    public TimeSpan AudioStartTime => TimeSpan.Zero;

    public TimeSpan BeginTime => TimeSpan.Zero;

    public TimeSpan DurationTime => TimeSpan.Zero;

    public IClock GlobalClock => this;
}
