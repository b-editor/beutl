using System.Globalization;
using Beutl.ProjectSystem;

namespace Beutl;

public enum GotoTimecodeError
{
    None,
    InvalidFormat,
    MarkerNotFound,
    NoScene,
    OutOfRange,
}

/// <summary>
/// Parses goto-timecode expressions used by the player's "edit current time" input.
/// </summary>
/// <remarks>
/// Accepted forms (case-insensitive where applicable):
/// <list type="bullet">
/// <item><c>@&lt;prefix&gt;</c> — first marker whose name starts with the prefix.</item>
/// <item><c>#&lt;int&gt;</c> or <c>&lt;int&gt;f</c> — absolute frame number.</item>
/// <item><c>+&lt;num&gt;{s|m|f}</c> or <c>-&lt;num&gt;{s|m|f}</c> — relative seek in seconds, minutes, or frames.</item>
/// <item><c>hh:mm:ss[.fff]</c> or <c>mm:ss[.fff]</c> — absolute time.</item>
/// </list>
/// </remarks>
public static class GotoTimecodeParser
{
    private static readonly string[] s_absoluteFormats =
    [
        @"hh\:mm\:ss\.fff",
        @"hh\:mm\:ss\.ff",
        @"hh\:mm\:ss\.f",
        @"hh\:mm\:ss",
        @"h\:mm\:ss\.fff",
        @"h\:mm\:ss\.ff",
        @"h\:mm\:ss\.f",
        @"h\:mm\:ss",
        @"mm\:ss\.fff",
        @"mm\:ss\.ff",
        @"mm\:ss\.f",
        @"mm\:ss",
        @"m\:ss\.fff",
        @"m\:ss\.ff",
        @"m\:ss\.f",
        @"m\:ss",
    ];

    /// <summary>
    /// Resolves a goto-timecode expression to a non-negative <see cref="TimeSpan"/> relative to the timeline origin.
    /// </summary>
    /// <param name="input">The user-entered expression (see class remarks for accepted forms).</param>
    /// <param name="frameRate">Frame rate used for frame-based and relative-frame inputs; must be positive.</param>
    /// <param name="currentTime">Current playhead time used as the base for relative expressions.</param>
    /// <param name="markers">Markers searched for <c>@</c>-prefixed inputs.</param>
    /// <param name="result">On success, the resolved time (clamped to <see cref="TimeSpan.Zero"/>).</param>
    /// <param name="error">On failure, the reason for the failure.</param>
    /// <returns><see langword="true"/> if <paramref name="input"/> resolved successfully.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="frameRate"/> is zero or negative.</exception>
    public static bool TryParse(
        string? input,
        int frameRate,
        TimeSpan currentTime,
        IReadOnlyList<SceneMarker> markers,
        out TimeSpan result,
        out GotoTimecodeError error)
    {
        result = TimeSpan.Zero;
        error = GotoTimecodeError.None;

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frameRate);

        if (string.IsNullOrWhiteSpace(input))
        {
            error = GotoTimecodeError.InvalidFormat;
            return false;
        }

        string text = input.Trim();

        if (text.StartsWith('@'))
        {
            return TryParseMarker(text[1..].Trim(), markers, out result, out error);
        }

        if (text.StartsWith('#'))
        {
            return TryParseFrame(text[1..].Trim(), frameRate, out result, out error);
        }

        if (text.Length > 0 && (text[0] == '+' || text[0] == '-'))
        {
            return TryParseRelative(text, frameRate, currentTime, out result, out error);
        }

        if (TryParseFrameSuffix(text, frameRate, out result))
        {
            return true;
        }

        if (TryParseAbsolute(text, out result))
        {
            ClampNonNegative(ref result);
            return true;
        }

        error = GotoTimecodeError.InvalidFormat;
        return false;
    }

    private static bool TryParseAbsolute(string text, out TimeSpan result)
    {
        if (TimeSpan.TryParseExact(text, s_absoluteFormats, CultureInfo.InvariantCulture, out result))
        {
            return true;
        }

        result = TimeSpan.Zero;
        return false;
    }

    private static bool TryParseFrameSuffix(string text, int frameRate, out TimeSpan result)
    {
        result = TimeSpan.Zero;
        if (text.Length < 2) return false;
        char last = text[^1];
        if (last != 'f' && last != 'F') return false;

        string numberPart = text[..^1].Trim();
        if (!int.TryParse(numberPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int frame))
        {
            return false;
        }

        if (frame < 0) frame = 0;
        result = frame.ToTimeSpan(frameRate);
        return true;
    }

    private static bool TryParseFrame(string text, int frameRate, out TimeSpan result, out GotoTimecodeError error)
    {
        result = TimeSpan.Zero;
        error = GotoTimecodeError.None;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int frame))
        {
            error = GotoTimecodeError.InvalidFormat;
            return false;
        }

        if (frame < 0) frame = 0;
        result = frame.ToTimeSpan(frameRate);
        return true;
    }

    private static bool TryParseRelative(
        string text,
        int frameRate,
        TimeSpan currentTime,
        out TimeSpan result,
        out GotoTimecodeError error)
    {
        result = TimeSpan.Zero;
        error = GotoTimecodeError.None;

        if (text.Length < 3)
        {
            error = GotoTimecodeError.InvalidFormat;
            return false;
        }

        int sign = text[0] == '-' ? -1 : 1;
        char unit = text[^1];
        string numberPart = text[1..^1].Trim();

        if (unit is not ('s' or 'S' or 'm' or 'M' or 'f' or 'F'))
        {
            error = GotoTimecodeError.InvalidFormat;
            return false;
        }

        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || double.IsNaN(value) || double.IsInfinity(value))
        {
            error = GotoTimecodeError.InvalidFormat;
            return false;
        }

        try
        {
            TimeSpan delta = unit switch
            {
                's' or 'S' => TimeSpan.FromSeconds(value),
                'm' or 'M' => TimeSpan.FromMinutes(value),
                _ /* 'f' or 'F' */ => value.ToTimeSpan(frameRate),
            };

            result = currentTime + (sign == -1 ? -delta : delta);
        }
        catch (OverflowException)
        {
            // The numeric value parsed but cannot be expressed as a TimeSpan
            // (either FromSeconds/FromMinutes overflowed, or the addition to
            // currentTime exceeded TimeSpan.MaxValue/MinValue).
            error = GotoTimecodeError.OutOfRange;
            return false;
        }

        ClampNonNegative(ref result);
        return true;
    }

    private static bool TryParseMarker(
        string name,
        IReadOnlyList<SceneMarker> markers,
        out TimeSpan result,
        out GotoTimecodeError error)
    {
        result = TimeSpan.Zero;
        error = GotoTimecodeError.None;

        if (name.Length == 0)
        {
            error = GotoTimecodeError.InvalidFormat;
            return false;
        }

        for (int i = 0; i < markers.Count; i++)
        {
            SceneMarker marker = markers[i];
            if (!string.IsNullOrEmpty(marker.Name)
                && marker.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
            {
                result = marker.Time;
                ClampNonNegative(ref result);
                return true;
            }
        }

        error = GotoTimecodeError.MarkerNotFound;
        return false;
    }

    private static void ClampNonNegative(ref TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
    }
}
