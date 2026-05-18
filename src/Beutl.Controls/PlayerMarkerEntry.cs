#nullable enable
namespace Beutl.Controls;

public sealed class PlayerMarkerEntry
{
    public PlayerMarkerEntry(string name, string timeText)
    {
        Name = name ?? string.Empty;
        TimeText = timeText ?? string.Empty;
    }

    public string Name { get; }

    public string TimeText { get; }
}
