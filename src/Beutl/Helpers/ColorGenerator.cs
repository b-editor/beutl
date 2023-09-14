using System.Security.Cryptography;
using System.Text;

using Avalonia.Media;

namespace Beutl.Helpers;

public static class ColorGenerator
{
    private static readonly Dictionary<string, Media.Color> s_cache = new();

    // https://qiita.com/pira/items/dd4057ef499154968f69
    public static Media.Color GenerateColor(string str)
    {
        if (!s_cache.TryGetValue(str, out Media.Color color))
        {
            try
            {
                byte[] utf8 = Encoding.UTF8.GetBytes(str);
                byte[] hash = MD5.HashData(utf8);
                Span<byte> hashSpan = hash.AsSpan(hash.Length - 7);

                int pos = hash.Length - 7;
                ReadOnlySpan<char> hashStr = Convert.ToHexString(hash.AsSpan().Slice(pos)).AsSpan();

                ReadOnlySpan<char> hueSpan = hashStr.Slice(0, 3);
                ReadOnlySpan<char> satSpan = hashStr.Slice(3, 2);
                ReadOnlySpan<char> valSpan = hashStr.Slice(5, 2);

                int hue = int.Parse(hueSpan, NumberStyles.HexNumber);
                int sat = int.Parse(satSpan, NumberStyles.HexNumber);
                int lit = int.Parse(valSpan, NumberStyles.HexNumber);

                var hsl = new HslColor(
                    1,
                    hue / 4095d * 360d,
                    (65 - (sat / 255d * 20d)) / 100d,
                    (75 - (lit / 255d * 20d)) / 100d);

                color = hsl.ToRgb().ToMedia();

                s_cache[str] = color;
            }
            catch
            {
                return Media.Colors.Teal;
            }
        }

        return color;
    }
}
