namespace Beutl.Animation;

public sealed class InstanceClock : IClock
{
    public InstanceClock()
    {
        GlobalClock = this;
    }

    public TimeSpan CurrentTime { get; set; }

    [Obsolete("Do not use this property.")]
    public TimeSpan AudioStartTime { get; set; }

    [Obsolete("Do not use this property.")]
    public TimeSpan AudioDurationTime { get; set; }

    public TimeSpan BeginTime { get; set; }

    public TimeSpan DurationTime { get; set; }

    public IClock GlobalClock { get; set; }
}
