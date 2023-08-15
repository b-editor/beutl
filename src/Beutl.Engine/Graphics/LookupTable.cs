namespace Beutl.Graphics;

public static partial class LookupTable
{
    internal static readonly byte[] s_linear;
    internal static readonly byte[] s_invert;

    static LookupTable()
    {
        s_linear = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            s_linear[i] = (byte)i;
        }

        s_invert = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            s_invert[i] = (byte)(255 - i);
        }
    }

    public static void Linear(byte[] data)
    {
        if (data.Length != 256)
            throw new ArgumentException("配列の長さが無効です", nameof(data));

        s_linear.CopyTo(data, 0);
    }

    public static void Invert(byte[] data)
    {
        if (data.Length != 256)
            throw new ArgumentException("配列の長さが無効です", nameof(data));

        s_invert.CopyTo(data, 0);
    }

    public static void SetStrength(float strength, byte[] data)
    {
        if (data.Length != 256)
            throw new ArgumentException("配列の長さが無効です", nameof(data));

        for (int i = 0; i < data.Length; i++)
        {
            float add = i * (1 - strength);
            data[i] = (byte)((data[i] * strength) + add);
        }
    }

    public static void SetStrength(float strength, (byte[] A, byte[] R, byte[] G, byte[] B) data)
    {
        if (data.A.Length != 256
            || data.R.Length != 256
            || data.G.Length != 256
            || data.B.Length != 256)
            throw new ArgumentException("配列の長さが無効です", nameof(data));

        for (int i = 0; i < 256; i++)
        {
            float add = i * (1 - strength);
            data.A[i] = (byte)((data.A[i] * strength) + add);
            data.R[i] = (byte)((data.R[i] * strength) + add);
            data.G[i] = (byte)((data.G[i] * strength) + add);
            data.B[i] = (byte)((data.B[i] * strength) + add);
        }
    }

    public static void Solarisation(byte[] data, int cycle = 2)
    {
        if (data.Length != 256)
            throw new ArgumentException("配列の長さが無効です", nameof(data));

        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((Math.Sin(i * cycle * Math.PI / 255) + 1) / 2 * 255);
        }
    }

    public static void Negaposi(byte[] data, byte value = 255)
    {
        if (data.Length != 256)
            throw new ArgumentException("配列の長さが無効です", nameof(data));

        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(value - i);
        }
    }

    public static void Negaposi((byte[] R, byte[] G, byte[] B) data, byte red = 255, byte green = 255, byte blue = 255)
    {
        if (data.R.Length != 256 || data.G.Length != 256 || data.B.Length != 256)
            throw new ArgumentException("配列の長さが無効です", nameof(data));

        if (red == green && green == blue)
        {
            Negaposi(data.R, red);
            data.R.CopyTo(data.G, 0);
            data.R.CopyTo(data.B, 0);
        }

        for (int i = 0; i < 256; i++)
        {
            data.R[i] = (byte)(red - i);
            data.G[i] = (byte)(green - i);
            data.B[i] = (byte)(blue - i);
        }
    }

    public static void Contrast(byte[] data, short contrast)
    {
        if (data.Length != 256)
            throw new ArgumentException("配列の長さが無効です", nameof(data));

        contrast = Math.Clamp(contrast, (short)-255, (short)255);

        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)Helper.Set255Round((1f + contrast / 255f) * (i - 128f) + 128f);
        }
    }

    public static void Gamma(byte[] data, float gamma)
    {
        if (data.Length != 256)
            throw new ArgumentException("配列の長さが無効です", nameof(data));

        gamma = Math.Clamp(gamma, 0.01f, 3f);

        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)Helper.Set255Round(MathF.Pow(i / 255f, 1f / gamma) * 255f);
        }
    }
}
