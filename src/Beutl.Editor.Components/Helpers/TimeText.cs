namespace Beutl.Editor.Components.Helpers;

// Times are shown and typed as TimeSpan text, as in earlier versions.
internal static class TimeText
{
    public static string Format(MediaTime time) => time.ToTimeSpan().ToString();

    public static bool TryParse(string? text, out MediaTime time)
    {
        time = default;
        if (!TimeSpan.TryParse(text, out TimeSpan value))
            return false;

        // A TimeSpan reaches past the range of MediaTime (about 414 years).
        try
        {
            time = MediaTime.FromTimeSpan(value);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
