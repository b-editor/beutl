namespace Beutl.Utilities;

public static class StringFormats
{
    // https://teratail.com/questions/136799#reply-207332
    /*
     * 1GB以上なら、1.5GBのように表示し、
     * 1MB以上～1GB未満ならば、789.2MBのように表示し、
     * 1KB以上～1MB未満ならば、300.5KBのように表示
     */
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
