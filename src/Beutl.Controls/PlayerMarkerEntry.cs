namespace Beutl.Controls;

internal sealed class PlayerMarkerEntry
{
    public PlayerMarkerEntry(string name, string timeText)
    {
        Name = name;
        TimeText = timeText;
    }

    public string Name { get; }

    public string TimeText { get; }
}
