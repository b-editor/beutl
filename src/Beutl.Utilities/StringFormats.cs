namespace Beutl.Utilities;

public static class StringFormats
{
    // https://teratail.com/questions/136799#reply-207332
    // Formats a byte count with a B, KB, MB or GB unit.
    public static string ToHumanReadableSize(double size, IFormatProvider? formatProvider = null)
    {
        return ToHumanReadableSizeCore(size, 0, formatProvider);
    }

    private static string ToHumanReadableSizeCore(double size, int scale, IFormatProvider? formatProvider)
    {
        string[] unit = ["B", "KB", "MB", "GB"];
        if (scale == unit.Length - 1 || size <= 1024)
        {
            FormattableString format = $"{size:F} {unit[scale]}";
            return format.ToString(formatProvider);
        }

        return ToHumanReadableSizeCore(size / 1024, scale + 1, formatProvider);
    }
}
