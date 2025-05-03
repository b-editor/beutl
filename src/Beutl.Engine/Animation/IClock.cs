namespace Beutl.Animation;

public interface IClock
{
    TimeSpan BeginTime { get; }

    TimeSpan DurationTime { get; }

    TimeSpan CurrentTime { get; }

    TimeSpan AudioStartTime { get; }

    IClock GlobalClock { get; }
}
