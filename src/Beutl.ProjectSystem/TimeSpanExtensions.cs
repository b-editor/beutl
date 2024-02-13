namespace Beutl;

public static class TimeSpanExtensions
{
    public static TimeSpan RoundToRate(this TimeSpan ts, double rate)
    {
        return Math.Round(ts.ToFrameNumber(rate), MidpointRounding.AwayFromZero).ToTimeSpan(rate);
    }

    public static TimeSpan FloorToRate(this TimeSpan ts, double rate)
    {
        return Math.Floor(ts.ToFrameNumber(rate)).ToTimeSpan(rate);
    }

    public static double ToFrameNumber(this TimeSpan ts, double rate)
    {
        return ts.TotalSeconds * rate;
    }
    
    public static double ToFrameNumber(this TimeSpan ts, int rate)
    {
        return ts.TotalSeconds * rate;
    }

    public static TimeSpan ToTimeSpan(this double f, double rate)
    {
        return TimeSpan.FromSeconds(f / rate);
    }

    public static TimeSpan ToTimeSpan(this int f, int rate)
    {
        return TimeSpan.FromTicks(TimeSpan.TicksPerSecond * f / rate);
    }
}
