namespace Beutl.Extensions.FFmpeg.PropertyEditors;

/// <summary>The selection a codec editor should apply: which index to select, and whether to reset the model value to the sentinel.</summary>
internal readonly record struct FormatSelectionResult(int SelectedIndex, bool ResetToSentinel);

/// <summary>
/// Decides which entry of an option list a codec editor should select. Shared by the pixel-format and
/// audio-format editors, which both prepend an "Auto" sentinel and reset to it when the current value
/// is no longer offered.
/// </summary>
internal static class CodecFormatSelection
{
    /// <param name="formatsWithSentinel">The option list with the "Auto" sentinel already prepended.</param>
    /// <returns>
    /// A reset to <paramref name="sentinel"/> (index 0) when <paramref name="current"/> is absent and is
    /// not itself the sentinel; otherwise the current value's index (0 when it is the sentinel).
    /// </returns>
    public static FormatSelectionResult Resolve<T>(T[] formatsWithSentinel, T current, T sentinel)
        where T : struct
    {
        int index = Array.IndexOf(formatsWithSentinel, current);
        return index < 0 && !EqualityComparer<T>.Default.Equals(current, sentinel)
            ? new FormatSelectionResult(0, ResetToSentinel: true)
            : new FormatSelectionResult(Math.Max(index, 0), ResetToSentinel: false);
    }
}
