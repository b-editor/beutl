using System.Buffers;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Beutl.Graphics;

public enum LookupTableDimension
{
    OneDimension = 1,

    ThreeDimension = 3,
}

public sealed unsafe partial class LookupTable : IDisposable
{
    internal static readonly byte[] s_linear;

    private static readonly Regex s_lutSizeReg = LUTSizeRegex();
    private static readonly Regex s_titleReg = TitleRegex();
    private static readonly Regex s_domainMinReg = DomainMinRegex();
    private static readonly Regex s_domainMaxReg = DomainMaxRegex();
    private readonly float[][] _arrays;

    public LookupTable(int length = 256, int lutsize = 256, LookupTableDimension dim = LookupTableDimension.OneDimension)
    {
        _arrays = new float[(int)dim][];

        for (int i = 0; i < _arrays.Length; i++)
        {
            _arrays[i] = ArrayPool<float>.Shared.Rent(length);
            Array.Clear(_arrays[i]);
        }

        Size = lutsize;
        Length = length;
        Dimension = dim;
    }

    static LookupTable()
    {
        s_linear = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            s_linear[i] = (byte)i;
        }
    }

    ~LookupTable()
    {
        Dispose();
    }

    public int Length { get; }

    public int Size { get; }

    public LookupTableDimension Dimension { get; }

    public bool IsDisposed { get; private set; }

    public static LookupTable Solarisation(int cycle = 2)
    {
        var table = new LookupTable();
        Span<float> data = table.AsSpan();
        for (int i = 0; i < 256; i++)
        {
            data[i] = (float)((Math.Sin(i * cycle * Math.PI / 255) + 1) / 2);
        }

        return table;
    }

    public static LookupTable Negaposi(byte value = 255)
    {
        var table = new LookupTable();

        Parallel.For(0, 256, pos =>
        {
            table.AsSpan()[pos] = (value - pos) / 256f;
        });

        return table;
    }

    public static LookupTable Negaposi(byte red = 255, byte green = 255, byte blue = 255)
    {
        if (red == green && green == blue) return Negaposi(red);

        var table = new LookupTable(256, 256, LookupTableDimension.ThreeDimension);
        Parallel.For(0, 256, pos =>
        {
            Span<float> rData = table.AsSpan(0);
            Span<float> gData = table.AsSpan(1);
            Span<float> bData = table.AsSpan(2);

            rData[pos] = (red - pos) / 256f;
            gData[pos] = (green - pos) / 256f;
            bData[pos] = (blue - pos) / 256f;
        });

        return table;
    }

    public static LookupTable Contrast(short contrast)
    {
        contrast = Math.Clamp(contrast, (short)-255, (short)255);
        var table = new LookupTable();

        Parallel.For(0, 256, pos =>
        {
            table.AsSpan()[pos] = Helper.Set255Round((1f + contrast / 255f) * (pos - 128f) + 128f) / 255f;
        });

        return table;
    }

    public static LookupTable Gamma(float gamma)
    {
        gamma = Math.Clamp(gamma, 0.01f, 3f);
        var table = new LookupTable();

        Parallel.For(0, 256, pos =>
        {
            table.AsSpan()[pos] = Helper.Set255Round(MathF.Pow(pos / 255f, 1f / gamma));
        });

        return table;
    }

    public static LookupTable Invert()
    {
        var table = new LookupTable();

        Parallel.For(0, 256, pos =>
        {
            table.AsSpan()[pos] = 1f - (pos / 255f);
        });

        return table;
    }

    public static LookupTable FromStream(Stream stream)
    {
        using var reader = new StreamReader(stream);
        int i = 0;
        ReadInfo(reader, out _, out LookupTableDimension dim, out int size, out _, out _);

        int length = (int)Math.Pow(size, (int)dim);
        var table = new LookupTable(length, size, dim);
        Span<float> rData = table.AsSpan(0);
        Span<float> gData = table.AsSpan(1);
        Span<float> bData = table.AsSpan(2);

        while (i < length)
        {
            string? line = reader.ReadLine();
            if (line is not null)
            {
                string[] values = line.Split(' ');
                if (values.Length != 3) continue;

                if (float.TryParse(values[0], out float r) &&
                    float.TryParse(values[1], out float g) &&
                    float.TryParse(values[2], out float b))
                {
                    rData[i] = r;
                    gData[i] = g;
                    bData[i] = b;
                    i++;
                }
            }
        }

        return table;
    }

    public static LookupTable FromCube(string file)
    {
        using var stream = new FileStream(file, FileMode.Open);

        return FromStream(stream);
    }

    public Span<float> AsSpan(int dimension = 0)
    {
        return _arrays[dimension].AsSpan();
    }

    private int Near(float x)
    {
        return Math.Min((int)(x + 0.5), Size - 1);
    }

    public byte[] ToByteArray(float strength, int dimension = 0)
    {
        float[] src = _arrays[dimension];
        byte[] dst = new byte[256];
        for (int i = 0; i < 256; i++)
        {
            float r = i * Size / 256f;
            float vec = src[Near(r)];

            dst[i] = (byte)((((vec * 255) + 0.5) * strength) + (i * (1 - strength)));
        }

        return dst;
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            foreach (float[] item in _arrays)
            {
                ArrayPool<float>.Shared.Return(item);
            }

            GC.SuppressFinalize(this);
            IsDisposed = true;
        }
    }

    private static void ReadInfo(StreamReader reader, out string title, out LookupTableDimension dim, out int size, out Vector3 min, out Vector3 max)
    {
        title = string.Empty;
        dim = LookupTableDimension.ThreeDimension;
        size = 33;
        min = new(0, 0, 0);
        max = new(1, 1, 1);
        bool titleFound = false;
        bool lutSizeFound = false;
        bool minFound = false;
        bool maxFound = false;

        while (!reader.EndOfStream)
        {
            if (titleFound && lutSizeFound && minFound && maxFound) break;
            string? line = reader.ReadLine();
            if (line is not null)
            {
                if (s_lutSizeReg.IsMatch(line))
                {
                    lutSizeFound = true;
                    Match match = s_lutSizeReg.Match(line);
                    size = int.Parse(match.Groups["size"].Value);
                    dim = match.Groups["dim"].Value is "3D" ? LookupTableDimension.ThreeDimension : LookupTableDimension.OneDimension;
                }
                else if (s_titleReg.IsMatch(line))
                {
                    titleFound = true;
                    Match match = s_lutSizeReg.Match(line);
                    title = match.Groups["text"].Value;
                }
                else if (s_domainMaxReg.IsMatch(line))
                {
                    maxFound = true;
                    Match match = s_domainMaxReg.Match(line);
                    float r = float.Parse(match.Groups["red"].Value);
                    float g = float.Parse(match.Groups["green"].Value);
                    float b = float.Parse(match.Groups["blue"].Value);
                    max = new(r, g, b);
                }
                else if (s_domainMinReg.IsMatch(line))
                {
                    minFound = true;
                    Match match = s_domainMinReg.Match(line);
                    float r = float.Parse(match.Groups["red"].Value);
                    float g = float.Parse(match.Groups["green"].Value);
                    float b = float.Parse(match.Groups["blue"].Value);
                    min = new(r, g, b);
                }
            }
        }

        reader.BaseStream.Position = 0;
    }

    [GeneratedRegex("^LUT_(?<dim>.*?)_SIZE (?<size>.*?)$")]
    private static partial Regex LUTSizeRegex();

    [GeneratedRegex("^TITLE \"(?<text>.*?)\"$")]
    private static partial Regex TitleRegex();

    [GeneratedRegex("^DOMAIN_MIN (?<red>.*?) (?<green>.*?) (?<blue>.*?)$")]
    private static partial Regex DomainMinRegex();

    [GeneratedRegex("^DOMAIN_MAX (?<red>.*?) (?<green>.*?) (?<blue>.*?)$")]
    private static partial Regex DomainMaxRegex();
}
