using System.Numerics;
using Beutl.Graphics;

namespace Beutl.Editor.Components.PropertyEditors.LutLibrary;

// Draws a small reference image with a LUT applied on the CPU, so a picker entry can show what the LUT does
// without rendering a frame. The shader in LutEffect works in the same sRGB domain this applies the LUT in.
internal static class LutThumbnailRenderer
{
    public const int Width = 128;
    public const int Height = 72;

    private const int HueBandHeight = 36;
    private const int GreyWidth = 96;
    private const int SkinWidth = 16;

    private static readonly (byte R, byte G, byte B) s_skin = (224, 172, 138);
    private static readonly (byte R, byte G, byte B) s_sky = (120, 170, 220);

    // A hue sweep above a grey ramp, with skin-tone and sky-blue patches, in RGBA.
    public static byte[] CreateReferenceImage()
    {
        var pixels = new byte[Width * Height * 4];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                (byte r, byte g, byte b) = y < HueBandHeight ? HueSweep(x) : BottomBand(x);
                int index = (y * Width + x) * 4;
                pixels[index] = r;
                pixels[index + 1] = g;
                pixels[index + 2] = b;
                pixels[index + 3] = 255;
            }
        }

        return pixels;
    }

    public static byte[] CreateImage(CubeFile cube)
    {
        byte[] reference = CreateReferenceImage();
        var result = new byte[reference.Length];
        Apply(cube, reference, result);
        return result;
    }

    public static void Apply(CubeFile cube, ReadOnlySpan<byte> source, Span<byte> destination)
    {
        Vector3 min = cube.Min;
        Vector3 max = cube.Max;
        Vector3 size = max - min;
        bool validDomain = size.X > 0 && size.Y > 0 && size.Z > 0
            && float.IsFinite(size.X) && float.IsFinite(size.Y) && float.IsFinite(size.Z);
        if (!validDomain)
        {
            min = Vector3.Zero;
            max = Vector3.One;
            size = Vector3.One;
        }

        for (int i = 0; i + 3 < source.Length; i += 4)
        {
            var input = new Vector3(
                (source[i] / 255f - min.X) / size.X,
                (source[i + 1] / 255f - min.Y) / size.Y,
                (source[i + 2] / 255f - min.Z) / size.Z);
            input = Vector3.Clamp(input, Vector3.Zero, Vector3.One);

            Vector3 output = cube.Dimention == CubeFileDimension.OneDimension
                ? Sample1D(cube, input)
                : Sample3D(cube, input);

            destination[i] = ToByte(output.X);
            destination[i + 1] = ToByte(output.Y);
            destination[i + 2] = ToByte(output.Z);
            destination[i + 3] = source[i + 3];
        }
    }

    private static (byte R, byte G, byte B) HueSweep(int x)
    {
        float hue = 360f * x / (Width - 1);
        return HsvToRgb(hue);
    }

    private static (byte R, byte G, byte B) HsvToRgb(float hue)
    {
        float h = hue % 360f / 60f;
        int sector = (int)h;
        float x = 1f - MathF.Abs(h % 2f - 1f);
        (float r, float g, float b) = sector switch
        {
            0 => (1f, x, 0f),
            1 => (x, 1f, 0f),
            2 => (0f, 1f, x),
            3 => (0f, x, 1f),
            4 => (x, 0f, 1f),
            _ => (1f, 0f, x),
        };

        return (ToByte(r), ToByte(g), ToByte(b));
    }

    private static (byte R, byte G, byte B) BottomBand(int x)
    {
        if (x < GreyWidth)
        {
            var grey = (byte)Math.Clamp((int)MathF.Round(255f * x / (GreyWidth - 1)), 0, 255);
            return (grey, grey, grey);
        }

        return x < GreyWidth + SkinWidth ? s_skin : s_sky;
    }

    private static Vector3 Sample3D(CubeFile cube, Vector3 input)
    {
        int size = cube.Size;
        int size2 = size * size;
        Vector3 position = input * (size - 1);
        int x = (int)position.X;
        int y = (int)position.Y;
        int z = (int)position.Z;
        float dx = position.X - x;
        float dy = position.Y - y;
        float dz = position.Z - z;

        int index = x + y * size + z * size2;
        int nextX = x == size - 1 ? 0 : 1;
        int nextY = y == size - 1 ? 0 : size;
        int nextZ = z == size - 1 ? 0 : size2;

        Vector3 v0 = cube.Data[index];
        Vector3 v1 = cube.Data[index + nextX];
        Vector3 v2 = cube.Data[index + nextX + nextY];
        Vector3 v3 = cube.Data[index + nextY];
        Vector3 v4 = cube.Data[index + nextZ];
        Vector3 v5 = cube.Data[index + nextX + nextZ];
        Vector3 v6 = cube.Data[index + nextX + nextY + nextZ];
        Vector3 v7 = cube.Data[index + nextY + nextZ];

        Vector3 s0 = v0 * (1f - dz) + v4 * dz;
        Vector3 s1 = v1 * (1f - dz) + v5 * dz;
        Vector3 s2 = v2 * (1f - dz) + v6 * dz;
        Vector3 s3 = v3 * (1f - dz) + v7 * dz;

        Vector3 l0 = s0 * (1f - dx) + s1 * dx;
        Vector3 l1 = s3 * (1f - dx) + s2 * dx;
        return l0 * (1f - dy) + l1 * dy;
    }

    private static Vector3 Sample1D(CubeFile cube, Vector3 input)
    {
        float maxIndex = cube.Size - 1;
        float ri = input.X * maxIndex;
        float gi = input.Y * maxIndex;
        float bi = input.Z * maxIndex;
        int r0 = (int)MathF.Floor(ri);
        int g0 = (int)MathF.Floor(gi);
        int b0 = (int)MathF.Floor(bi);
        int r1 = Math.Min(r0 + 1, cube.Size - 1);
        int g1 = Math.Min(g0 + 1, cube.Size - 1);
        int b1 = Math.Min(b0 + 1, cube.Size - 1);

        return new Vector3(
            Lerp(cube.Data[r0].X, cube.Data[r1].X, ri - r0),
            Lerp(cube.Data[g0].Y, cube.Data[g1].Y, gi - g0),
            Lerp(cube.Data[b0].Z, cube.Data[b1].Z, bi - b0));
    }

    private static float Lerp(float from, float to, float amount) => from + (to - from) * amount;

    private static byte ToByte(float value)
    {
        if (!float.IsFinite(value))
            return 0;

        return (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
    }
}
