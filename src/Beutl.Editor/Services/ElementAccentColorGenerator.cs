using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Beutl.Media;

namespace Beutl.Editor.Services;

public static class ElementAccentColorGenerator
{
    private static readonly ConcurrentDictionary<string, Color> s_cache = new(StringComparer.Ordinal);

    // https://qiita.com/pira/items/dd4057ef499154968f69
    public static Color GenerateColor(string key)
    {
        return s_cache.GetOrAdd(key, static value =>
        {
            try
            {
                byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes(value));
                ReadOnlySpan<char> hashStr = Convert.ToHexString(hash.AsSpan(hash.Length - 7)).AsSpan();
                int hue = int.Parse(hashStr[..3], NumberStyles.HexNumber);
                int saturation = int.Parse(hashStr.Slice(3, 2), NumberStyles.HexNumber);
                int lightness = int.Parse(hashStr.Slice(5, 2), NumberStyles.HexNumber);

                return FromHsl(
                    hue / 4095d * 360d,
                    (65 - (saturation / 255d * 20d)) / 100d,
                    (75 - (lightness / 255d * 20d)) / 100d);
            }
            catch
            {
                return Colors.Teal;
            }
        });
    }

    private static Color FromHsl(double hue, double saturation, double lightness)
    {
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double sector = hue / 60d;
        double x = chroma * (1 - Math.Abs(sector % 2 - 1));
        double m = lightness - chroma / 2;
        (double r, double g, double b) = ((int)sector % 6) switch
        {
            0 => (chroma, x, 0d),
            1 => (x, chroma, 0d),
            2 => (0d, chroma, x),
            3 => (0d, x, chroma),
            4 => (x, 0d, chroma),
            _ => (chroma, 0d, x)
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
