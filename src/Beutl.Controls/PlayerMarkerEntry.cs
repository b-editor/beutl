using Avalonia.Media;

namespace Beutl.Controls;

public sealed class PlayerMarkerEntry
{
    public PlayerMarkerEntry(string name, string timeText)
    {
        Name = name;
        TimeText = timeText;
    }

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
