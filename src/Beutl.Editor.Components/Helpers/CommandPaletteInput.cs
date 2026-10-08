namespace Beutl.Editor.Components.Helpers;

/// <summary>Pieces shared by the commands that ask for their arguments in the command palette.</summary>
public static class CommandPaletteInput
{
    /// <summary>The format the player shows the playhead in.</summary>
    public const string TimecodeFormat = @"hh\:mm\:ss\.ff";

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
