namespace Beutl.Utilities;

public static class StringFormats
{
    // https://teratail.com/questions/136799#reply-207332
    // Formats a byte count with a B, KB, MB or GB unit.
    public static string ToHumanReadableSize(double size, int scale = 0, int standard = 1024, IFormatProvider? formatProvider = null)
    {
        string[] unit = ["B", "KB", "MB", "GB"];
        if (scale == unit.Length - 1 || size <= standard)
        {
            FormattableString format = $"{size:F} {unit[scale]}";
            return format.ToString(formatProvider);
        }

        return ToHumanReadableSize(size / standard, scale + 1, standard, formatProvider);
    }
}
