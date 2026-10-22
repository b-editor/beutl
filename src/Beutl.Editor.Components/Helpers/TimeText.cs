using Beutl.JsonConverters;

namespace Beutl.Editor.Components.Helpers;

// Times are shown and typed as TimeSpan text, as in earlier versions.
internal static class TimeText
{
    public static string Format(MediaTime time) => time.ToTimeSpan().ToString();

    public static bool TryParse(string? text, out MediaTime time) => TryParse(text, out time, out _);

    // Text left as Format showed `shown` reads as it; typed text snaps to the frame grid of `rate`, as file times do.
    public static bool TryParse(string? text, MediaTime shown, Rational rate, out MediaTime time)
    {
        if (text == Format(shown))
        {
            time = shown;
            return true;
        }

        if (!TryParse(text, out time, out long ticks))
            return false;

        if (MediaTimeJsonConverter.TryGetFrameNearTicks(ticks, rate, out MediaTime frame))
            time = frame;
        return true;
    }

    private static bool TryParse(string? text, out MediaTime time, out long ticks)
    {
        time = default;
        ticks = 0;
        if (!TimeSpan.TryParse(text, out TimeSpan value))
            return false;

        // A TimeSpan reaches past the range of MediaTime (about 414 years).
        try
        {
            time = MediaTime.FromTimeSpan(value);
            ticks = value.Ticks;
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
