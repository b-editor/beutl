using System.Numerics;
using System.Security.Cryptography;
using System.Text;

using Avalonia.Media;

using Beutl.Utilities;

namespace Beutl.Helpers;

public static class ColorGenerator
{
    private static readonly Dictionary<string, Media.Color> s_cache = [];

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

    // https://github.com/google/skia/blob/0d39172f35d259b6ab888974177bc4e6d839d44c/src/effects/SkHighContrastFilter.cpp
    public static Color GetTextColor(Color color)
    {
        static Vector3 Mix(Vector3 x, Vector3 y, float a)
        {
            return (x * (1 - a)) + (y * a);
        }

        static Vector3 Saturate(Vector3 a)
        {
            return Vector3.Clamp(a, new(0), new(1));
        }

        static Color ToColor(Vector3 vector)
        {
            return new(255, (byte)(vector.X * 255), (byte)(vector.Y * 255), (byte)(vector.Z * 255));
        }

        static Vector3 ToVector3(Color color)
        {
            return new Vector3(color.R / 255f, color.G / 255f, color.B / 255f);
        }

        // 計算機イプシロン
        // 'float.Epsilon'は使わないで
        const float Epsilon = MathUtilities.FloatEpsilon;
        float contrast = 1.0f;
        contrast = Math.Max(-1.0f + Epsilon, Math.Min(contrast, +1.0f - Epsilon));

        contrast = (1.0f + contrast) / (1.0f - contrast);

        Vector3 c = ToVector3(color);
        float grayscale = Vector3.Dot(new(0.2126f, 0.7152f, 0.0722f), c);
        c = new Vector3(grayscale);

        // brightness
        //c = Vector3.One - c;

        //lightness
        HslColor hsl = ToColor(c).ToHsl();
        c = ToVector3(HslColor.ToRgb(hsl.H, hsl.S, 1 - hsl.L, hsl.A));

        c = Mix(new Vector3(0.5f), c, contrast);
        c = Saturate(c);

        return ToColor(c);
    }
}
