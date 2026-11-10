using Beutl.Configuration;

namespace Beutl.Editor.Components.Helpers;

/// <summary>Pieces shared by the commands that ask for their arguments in the command palette.</summary>
public static class CommandPaletteInput
{
    /// <summary>
    /// Formats <paramref name="time"/> in <paramref name="format"/> so that it parses back to the same frame. A
    /// timecode or a frame number names the frame itself. A time gets milliseconds, which keep a frame apart even
    /// at 120 fps, and the day from 24 hours on instead of wrapping to the first day.
    /// </summary>
    /// <remarks>
    /// <paramref name="time"/> is a timeline position (the playhead or a marker), never negative. No sign is
    /// written: <see cref="GotoTimecodeParser"/> reads a leading minus as a relative seek.
    /// </remarks>
    public static string FormatTimecode(MediaTime time, Rational frameRate, TimeDisplayFormat format)
    {
        if (format != TimeDisplayFormat.Time)
            return TimeDisplayFormatter.Format(time, frameRate, format);

        TimeSpan value = time.ToTimeSpan();
        return value.ToString(value.Days > 0 ? @"d\.hh\:mm\:ss\.fff" : @"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
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
