using System.Buffers;
using System.Numerics;
using System.Text.RegularExpressions;

namespace BEditorNext.Graphics;

public enum LookupTableDimension
{
    OneDimension = 1,

    ThreeDimension = 3,
}

public sealed unsafe class LookupTable : IDisposable
{
    private static readonly Regex _lutSizeReg = new("^LUT_(?<dim>.*?)_SIZE (?<size>.*?)$");
    private static readonly Regex _titleReg = new("^TITLE \"(?<text>.*?)\"$");
    private static readonly Regex _domainMinReg = new("^DOMAIN_MIN (?<red>.*?) (?<green>.*?) (?<blue>.*?)$");
    private static readonly Regex _domainMaxReg = new("^DOMAIN_MAX (?<red>.*?) (?<green>.*?) (?<blue>.*?)$");
    private readonly float[][] _arrays;

    public LookupTable(int length = 256, int lutsize = 256, LookupTableDimension dim = LookupTableDimension.OneDimension)
    {
        _arrays = new float[(int)dim][];

        for (var i = 0; i < _arrays.Length; i++)
        {
            _arrays[i] = ArrayPool<float>.Shared.Rent(length);
            Array.Clear(_arrays[i]);
        }

        Size = lutsize;
        Length = length;
        Dimension = dim;
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
        var data = table.AsSpan();
        for (var i = 0; i < 256; i++)
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
            var rData = table.AsSpan(0);
            var gData = table.AsSpan(1);
            var bData = table.AsSpan(2);

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
            table.AsSpan()[pos] = Effects.Helper.Set255Round(((1f + (contrast / 255f)) * (pos - 128f)) + 128f) / 255f;
        });

        return table;
    }

    public static LookupTable Gamma(float gamma)
    {
        gamma = Math.Clamp(gamma, 0.01f, 3f);
        var table = new LookupTable();

        Parallel.For(0, 256, pos =>
        {
            table.AsSpan()[pos] = Effects.Helper.Set255Round(MathF.Pow(pos / 255f, 1f / gamma));
        });

        return table;
    }

    public static LookupTable FromStream(Stream stream)
    {
        using var reader = new StreamReader(stream);
        var i = 0;
        ReadInfo(reader, out _, out var dim, out var size, out _, out _);

        var length = (int)Math.Pow(size, (int)dim);
        var table = new LookupTable(length, size, dim);
        var rData = table.AsSpan(0);
        var gData = table.AsSpan(1);
        var bData = table.AsSpan(2);

        while (i < length)
        {
            var line = reader.ReadLine();
            if (line is not null)
            {
                var values = line.Split(' ');
                if (values.Length != 3) continue;

                if (float.TryParse(values[0], out var r) &&
                    float.TryParse(values[1], out var g) &&
                    float.TryParse(values[2], out var b))
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

    public void Dispose()
    {
        if (!IsDisposed)
        {
            foreach (var item in _arrays)
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
        var titleFound = false;
        var lutSizeFound = false;
        var minFound = false;
        var maxFound = false;

        while (!reader.EndOfStream)
        {
            if (titleFound && lutSizeFound && minFound && maxFound) break;
            var line = reader.ReadLine();
            if (line is not null)
            {
                if (_lutSizeReg.IsMatch(line))
                {
                    lutSizeFound = true;
                    var match = _lutSizeReg.Match(line);
                    size = int.Parse(match.Groups["size"].Value);
                    dim = match.Groups["dim"].Value is "3D" ? LookupTableDimension.ThreeDimension : LookupTableDimension.OneDimension;
                }
                else if (_titleReg.IsMatch(line))
                {
                    titleFound = true;
                    var match = _lutSizeReg.Match(line);
                    title = match.Groups["text"].Value;
                }
                else if (_domainMaxReg.IsMatch(line))
                {
                    maxFound = true;
                    var match = _domainMaxReg.Match(line);
                    var r = float.Parse(match.Groups["red"].Value);
                    var g = float.Parse(match.Groups["green"].Value);
                    var b = float.Parse(match.Groups["blue"].Value);
                    max = new(r, g, b);
                }
                else if (_domainMinReg.IsMatch(line))
                {
                    minFound = true;
                    var match = _domainMinReg.Match(line);
                    var r = float.Parse(match.Groups["red"].Value);
                    var g = float.Parse(match.Groups["green"].Value);
                    var b = float.Parse(match.Groups["blue"].Value);
                    min = new(r, g, b);
                }
            }
        }

        reader.BaseStream.Position = 0;
    }
}
