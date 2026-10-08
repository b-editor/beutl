namespace Beutl.Editor.Components.Helpers;

/// <summary>Pieces shared by the commands that ask for their arguments in the command palette.</summary>
public static class CommandPaletteInput
{
    /// <summary>
    /// Formats <paramref name="time"/> so that it parses back to the same frame: milliseconds keep a frame
    /// apart even at 120 fps, and the day is added from 24 hours on instead of wrapping to the first day.
    /// </summary>
    public static string FormatTimecode(TimeSpan time)
    {
        return time.ToString(time.Days > 0 ? @"d\.hh\:mm\:ss\.fff" : @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
    }

    public static string? Required(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? Strings.CommandPalette_ValueRequired : null;
    }

    /// <summary>
    /// Describes the choice holding the current value; a pick list always starts on its first item,
    /// so the current one has to be marked instead.
    /// </summary>
    public static string? DescribeCurrent(bool isCurrent)
    {
        return isCurrent ? Strings.Current : null;
    }
}
