using System.Globalization;
using Beutl.ProjectSystem;

namespace Beutl;

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

    public static bool TryParse(
        string? input,
        int frameRate,
        TimeSpan currentTime,
        IReadOnlyList<SceneMarker> markers,
        out TimeSpan result,
        out string? error)
    {
        result = TimeSpan.Zero;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "GotoTimecode_InvalidFormat";
            return false;
        }

        if (frameRate <= 0)
        {
            frameRate = 30;
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

        error = "GotoTimecode_InvalidFormat";
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

    private static bool TryParseFrame(string text, int frameRate, out TimeSpan result, out string? error)
    {
        result = TimeSpan.Zero;
        error = null;
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int frame))
        {
            error = "GotoTimecode_InvalidFormat";
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
        out string? error)
    {
        result = TimeSpan.Zero;
        error = null;

        if (text.Length < 3)
        {
            error = "GotoTimecode_InvalidFormat";
            return false;
        }

        int sign = text[0] == '-' ? -1 : 1;
        char unit = text[^1];
        string numberPart = text[1..^1].Trim();

        if (!double.TryParse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || double.IsNaN(value) || double.IsInfinity(value))
        {
            error = "GotoTimecode_InvalidFormat";
            return false;
        }

        try
        {
            TimeSpan delta = unit switch
            {
                's' or 'S' => TimeSpan.FromSeconds(value),
                'm' or 'M' => TimeSpan.FromMinutes(value),
                'f' or 'F' => value.ToTimeSpan(frameRate),
                _ => throw new FormatException(),
            };

            result = currentTime + (sign == -1 ? -delta : delta);
        }
        catch (Exception ex) when (ex is OverflowException or FormatException or ArgumentException)
        {
            error = "GotoTimecode_InvalidFormat";
            return false;
        }

        ClampNonNegative(ref result);
        return true;
    }

    private static bool TryParseMarker(
        string name,
        IReadOnlyList<SceneMarker> markers,
        out TimeSpan result,
        out string? error)
    {
        result = TimeSpan.Zero;
        error = null;

        if (name.Length == 0)
        {
            error = "GotoTimecode_InvalidFormat";
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

        error = "GotoTimecode_MarkerNotFound";
        return false;
    }

    private static void ClampNonNegative(ref TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
    }

    public static TimeSpan ClampToSceneRange(TimeSpan ts, TimeSpan sceneStart, TimeSpan sceneDuration, int frameRate)
    {
        if (frameRate <= 0) frameRate = 30;

        TimeSpan frame = TimeSpan.FromSeconds(1d / frameRate);
        TimeSpan max = sceneStart + sceneDuration - frame;
        if (max < sceneStart) max = sceneStart;
        if (ts < sceneStart) return sceneStart;
        if (ts > max) return max;
        return ts;
    }
}
