using System.Globalization;
using System.Text;

namespace Beutl.Editor.Services.Captions;

internal static class CaptionCodecUtilities
{
    private static readonly UTF8Encoding s_utf8 = new(false, true);

    public static void EnsureCueCanExport(CaptionCue cue, int cueIndex)
    {
        if (cue.Start < TimeSpan.Zero)
            throw new CaptionExportException(cueIndex, "Cue start time cannot be negative.");

        if (cue.End <= cue.Start)
            throw new CaptionExportException(cueIndex, "Cue end time must be after its start time.");

        EnsureValidUnicode(cue.Text, cueIndex, "text");
        EnsureValidUnicode(cue.Speaker, cueIndex, "speaker");
        EnsureValidUnicode(cue.Language, cueIndex, "language");
        foreach ((string key, string value) in cue.Metadata)
        {
            EnsureValidUnicode(key, cueIndex, $"metadata key '{key}'");
            EnsureValidUnicode(value, cueIndex, $"metadata value '{key}'");
        }
    }

    public static void EnsureNoBlankCueLines(string text, int cueIndex, CaptionFormatId format)
    {
        if (text.Length == 0)
            return;

        if (CaptionTextUtilities.GetLines(text).Any(line => line.Length == 0))
        {
            throw new CaptionExportException(
                cueIndex,
                $"{format} cannot represent a blank line inside one cue without ending the cue block.");
        }
    }

    public static bool TrySplitTimingLine(
        string value,
        out string start,
        out string end)
    {
        int separator = value.IndexOf("-->", StringComparison.Ordinal);
        if (separator < 0
            || value.IndexOf("-->", separator + 3, StringComparison.Ordinal) >= 0)
        {
            start = string.Empty;
            end = string.Empty;
            return false;
        }

        start = value[..separator].Trim();
        ReadOnlySpan<char> right = value.AsSpan(separator + 3).TrimStart();
        int whitespace = right.IndexOfAny(' ', '\t');
        end = (whitespace >= 0 ? right[..whitespace] : right).ToString();
        return start.Length > 0 && end.Length > 0;
    }

    public static bool TryParseSrtTime(string value, out TimeSpan result)
        => TryParseTime(value, ',', 3, allowOmittedHours: false, minimumHourDigits: 2, out result);

    public static bool TryParseWebVttTime(string value, out TimeSpan result)
        => TryParseTime(value, '.', 3, allowOmittedHours: true, minimumHourDigits: 2, out result);

    public static bool TryParseAssTime(string value, out TimeSpan result)
        => TryParseTime(value, '.', 2, allowOmittedHours: false, minimumHourDigits: 1, out result);

    public static (TimeSpan Start, TimeSpan End) QuantizeCue(
        CaptionCue cue,
        int cueIndex,
        long resolutionTicks)
    {
        EnsureCueCanExport(cue, cueIndex);

        long startTicks = cue.Start.Ticks - cue.Start.Ticks % resolutionTicks;
        long endTicks = cue.End.Ticks;
        long remainder = endTicks % resolutionTicks;
        if (remainder != 0)
        {
            long increment = resolutionTicks - remainder;
            endTicks = endTicks <= TimeSpan.MaxValue.Ticks - increment
                ? endTicks + increment
                : endTicks - remainder;
        }

        if (endTicks <= startTicks)
        {
            throw new CaptionExportException(
                cueIndex,
                "The cue is too short to represent at the target format's timing precision.");
        }

        return (new TimeSpan(startTicks), new TimeSpan(endTicks));
    }

    public static string FormatSrtTime(TimeSpan value)
        => FormatTime(value, ',', 3);

    public static string FormatWebVttTime(TimeSpan value)
        => FormatTime(value, '.', 3);

    public static string FormatAssTime(TimeSpan value)
        => FormatTime(value, '.', 2, minimumHourDigits: 1);

    private static bool TryParseTime(
        string value,
        char fractionSeparator,
        int fractionDigits,
        bool allowOmittedHours,
        int minimumHourDigits,
        out TimeSpan result)
    {
        result = default;
        value = value.Trim();

        int fractionIndex = value.LastIndexOf(fractionSeparator);
        if (fractionIndex < 0 || fractionIndex != value.Length - fractionDigits - 1)
            return false;

        ReadOnlySpan<char> fractionSpan = value.AsSpan(fractionIndex + 1);
        if (!IsAsciiDigits(fractionSpan)
            || !int.TryParse(fractionSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int fraction))
        {
            return false;
        }

        string[] clockParts = value[..fractionIndex].Split(':');
        if (clockParts.Length != 3 && (!allowOmittedHours || clockParts.Length != 2))
            return false;

        ReadOnlySpan<char> hoursSpan;
        ReadOnlySpan<char> minutesSpan;
        ReadOnlySpan<char> secondsSpan;
        if (clockParts.Length == 3)
        {
            hoursSpan = clockParts[0];
            minutesSpan = clockParts[1];
            secondsSpan = clockParts[2];
        }
        else
        {
            hoursSpan = "0";
            minutesSpan = clockParts[0];
            secondsSpan = clockParts[1];
        }

        if (hoursSpan.Length < (clockParts.Length == 3 ? minimumHourDigits : 1)
            || minutesSpan.Length != 2
            || secondsSpan.Length != 2
            || !IsAsciiDigits(hoursSpan)
            || !IsAsciiDigits(minutesSpan)
            || !IsAsciiDigits(secondsSpan)
            || !long.TryParse(hoursSpan, NumberStyles.None, CultureInfo.InvariantCulture, out long hours)
            || !int.TryParse(minutesSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
            || !int.TryParse(secondsSpan, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds)
            || minutes > 59
            || seconds > 59)
        {
            return false;
        }

        try
        {
            long totalSeconds = checked(checked(hours * 60 + minutes) * 60 + seconds);
            long fractionTicks = fractionDigits == 3
                ? fraction * TimeSpan.TicksPerMillisecond
                : fraction * (TimeSpan.TicksPerSecond / 100);
            long ticks = checked(totalSeconds * TimeSpan.TicksPerSecond + fractionTicks);
            if (ticks > TimeSpan.MaxValue.Ticks)
                return false;

            result = new TimeSpan(ticks);
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static string FormatTime(
        TimeSpan value,
        char fractionSeparator,
        int fractionDigits,
        int minimumHourDigits = 2)
    {
        long hours = value.Ticks / TimeSpan.TicksPerHour;
        int minutes = value.Minutes;
        int seconds = value.Seconds;
        long fraction = fractionDigits == 3
            ? value.Ticks % TimeSpan.TicksPerSecond / TimeSpan.TicksPerMillisecond
            : value.Ticks % TimeSpan.TicksPerSecond / (TimeSpan.TicksPerSecond / 100);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{hours.ToString(new string('0', minimumHourDigits), CultureInfo.InvariantCulture)}:{minutes:00}:{seconds:00}{fractionSeparator}{fraction.ToString(new string('0', fractionDigits), CultureInfo.InvariantCulture)}");
    }

    private static bool IsAsciiDigits(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return false;

        foreach (char character in value)
        {
            if (character is < '0' or > '9')
                return false;
        }

        return true;
    }

    private static void EnsureValidUnicode(string? value, int cueIndex, string fieldName)
    {
        if (value is null)
            return;

        try
        {
            _ = s_utf8.GetByteCount(value);
        }
        catch (EncoderFallbackException ex)
        {
            throw new CaptionExportException(
                cueIndex,
                $"Cue {fieldName} contains invalid Unicode.",
                ex);
        }
    }
}
