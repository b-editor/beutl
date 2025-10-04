namespace Beutl.Animation;

public interface IClock
{
    TimeSpan BeginTime { get; }

    TimeSpan DurationTime { get; }

    TimeSpan CurrentTime { get; }

    [Obsolete("Do not use this property.")]
    TimeSpan AudioStartTime { get; }

    [Obsolete("Do not use this property.")]
    TimeSpan AudioDurationTime { get; }

    IClock GlobalClock { get; }
}
