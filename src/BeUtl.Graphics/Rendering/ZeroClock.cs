using BeUtl.Animation;

namespace BeUtl.Rendering;

internal sealed class ZeroClock : IClock
{
    public static readonly ZeroClock Instance = new();

    public TimeSpan CurrentTime => TimeSpan.Zero;
}
