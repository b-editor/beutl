using System.Globalization;

using Avalonia.Media;

namespace Beutl.Controls;

public sealed class PlayerMarkerEntry
{
    public PlayerMarkerEntry(string name, string timeText)
    {
        Name = name;
        TimeText = timeText;
    }

    // Written as times were before the editor could show them as timecodes or frame numbers.
    public PlayerMarkerEntry(string name, MediaTime time, Color color)
        : this(name, time.ToTimeSpan().ToString(@"hh\:mm\:ss\.ff", CultureInfo.InvariantCulture), time, color)
    {
    }

    /// <summary>Creates a marker that the seek bar marks at <paramref name="time"/> and the list shows at <paramref name="timeText"/>.</summary>
    public PlayerMarkerEntry(string name, string timeText, MediaTime time, Color color)
        : this(name, timeText)
    {
        Time = time;
        Color = color;
    }

    public string Name { get; }

    public string TimeText { get; }

    /// <summary>Gets where the marker lies, or null for one that the seek bar does not mark.</summary>
    public MediaTime? Time { get; }

    /// <summary>Gets the color of the marker's mark on the seek bar.</summary>
    public Color Color { get; }
}
