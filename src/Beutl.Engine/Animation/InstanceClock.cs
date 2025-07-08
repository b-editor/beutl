namespace Beutl.Animation;

public sealed class InstanceClock : IClock
{
    public InstanceClock()
    {
        GlobalClock = this;
    }

    public TimeSpan CurrentTime { get; set; }

    public TimeSpan AudioStartTime { get; set; }

    public TimeSpan AudioDurationTime { get; set; }

    public TimeSpan BeginTime { get; set; }

    public TimeSpan DurationTime { get; set; }

    public IClock GlobalClock { get; set; }
}
