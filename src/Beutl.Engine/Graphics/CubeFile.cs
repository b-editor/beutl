using System.Numerics;
using System.Text.RegularExpressions;

using OpenCvSharp;

namespace Beutl.Graphics;

public partial class CubeFile : IEquatable<CubeFile>
{
    private static readonly Regex s_lutSizeReg = LUTSizeRegex();
    private static readonly Regex s_titleReg = TitleRegex();
    private static readonly Regex s_domainMinReg = DomainMinRegex();
    private static readonly Regex s_domainMaxReg = DomainMaxRegex();

    public required string Title { get; init; }

    public required CubeFileDimension Dimention { get; init; }

    public required int Size { get; init; }

    public required Vector3 Min { get; init; }

    public required Vector3 Max { get; init; }

    public required Vector3[] Data { get; init; }

    private int Near(float x)
    {
        return Math.Min((int)x, Size - 1);
    }

    // https://shizenkarasuzon.hatenablog.com/entry/2020/08/13/185223#1DLUT%E3%81%A83DLUT
    public Vector3 TrilinearInterplate(Vec3b color)
    {
        Span<byte> pos = stackalloc byte[3]; // 0~33
        Span<float> delta = stackalloc float[3]; //
        int lut_size = Size;
        int lut_size_2 = lut_size * lut_size;

        pos[0] = (byte)(color[0] * lut_size / 256);
        pos[1] = (byte)(color[1] * lut_size / 256);
        pos[2] = (byte)(color[2] * lut_size / 256);

        delta[0] = color[0] * lut_size / 256.0f - pos[0];
        delta[1] = color[1] * lut_size / 256.0f - pos[1];
        delta[2] = color[2] * lut_size / 256.0f - pos[2];

        Span<Vector3> vertex_color = stackalloc Vector3[8];
        Span<Vector3> surf_color = stackalloc Vector3[4];
        Span<Vector3> line_color = stackalloc Vector3[2];
        Vector3 out_color;

        int index = pos[0] + pos[1] * lut_size + pos[2] * lut_size_2;

        Span<int> next_index =
        [
            1,
            lut_size,
            lut_size_2
        ];

        if (index % lut_size == lut_size - 1)
        {
            next_index[0] = 0;
        }
        if (index / lut_size % lut_size == lut_size - 1)
        {
            next_index[1] = 0;
        }
        if (index / lut_size_2 % lut_size == lut_size - 1)
        {
            next_index[2] = 0;
        }

        // https://en.wikipedia.org/wiki/Trilinear_interpolation
        vertex_color[0] = Data[index];
        vertex_color[1] = Data[index + next_index[0]];
        vertex_color[2] = Data[index + next_index[0] + next_index[1]];
        vertex_color[3] = Data[index + next_index[1]];
        vertex_color[4] = Data[index + next_index[2]];
        vertex_color[5] = Data[index + next_index[0] + next_index[2]];
        vertex_color[6] = Data[index + next_index[0] + next_index[1] + next_index[2]];
        vertex_color[7] = Data[index + next_index[1] + next_index[2]];

        surf_color[0] = vertex_color[0] * (1.0f - delta[2]) + vertex_color[4] * delta[2];
        surf_color[1] = vertex_color[1] * (1.0f - delta[2]) + vertex_color[5] * delta[2];
        surf_color[2] = vertex_color[2] * (1.0f - delta[2]) + vertex_color[6] * delta[2];
        surf_color[3] = vertex_color[3] * (1.0f - delta[2]) + vertex_color[7] * delta[2];

        line_color[0] = surf_color[0] * (1.0f - delta[0]) + surf_color[1] * delta[0];
        line_color[1] = surf_color[2] * (1.0f - delta[0]) + surf_color[3] * delta[0];

        out_color = line_color[0] * (1.0f - delta[1]) + line_color[1] * delta[1];

        return out_color;
    }

    private Vector3 LinearInterplate(float p)
    {
        Vector3 near = Data[Near(p)];
        Vector3 neighbor = Data[Near(p + 1)];
        if (near == neighbor)
        {
            return near;
        }
        else
        {
            float progress = p - Near(p);

            return ((neighbor - near) * progress) + near;
        }
    }

    public void ToLUT(float strength, byte[] r, byte[] g, byte[] b)
    {
        if (Dimention != CubeFileDimension.OneDimension)
            throw new InvalidOperationException("ToLUTメソッドは1D LUTでのみ使えます。");

        if (r.Length != 256 || g.Length != 256 || b.Length != 256)
            throw new ArgumentException("配列の長さが無効です。");

        int size = Size;
        for (int i = 0; i < 256; i++)
        {
            float p = i * size / 256f;
            Vector3 v = LinearInterplate(p);

            float add = i * (1 - strength);
            r[i] = (byte)((((v.X * 255) + 0.5) * strength) + add);
            g[i] = (byte)((((v.Y * 255) + 0.5) * strength) + add);
            b[i] = (byte)((((v.Z * 255) + 0.5) * strength) + add);
        }
    }

    public static CubeFile FromStream(Stream stream)
    {
        using var reader = new StreamReader(stream);
        int i = 0;
        ReadInfo(
            reader,
            out string? title,
            out CubeFileDimension dim,
            out int size,
            out Vector3 min,
            out Vector3 max);

        int length = (int)Math.Pow(size, (int)dim);
        Vector3[] data = new Vector3[length];

        while (i < length)
        {
            string? line = reader.ReadLine();
            if (line is not null && !line.StartsWith('#'))
            {
                string[] values = line.Split(' ');
                if (values.Length != 3) continue;

                if (float.TryParse(values[0], out float r) &&
                    float.TryParse(values[1], out float g) &&
                    float.TryParse(values[2], out float b))
                {
                    data[i] = new(r, g, b);
                    i++;
                }
            }
        }

        return new CubeFile
        {
            Title = title,
            Dimention = dim,
            Size = size,
            Min = min,
            Max = max,
            Data = data
        };
    }

    private static void ReadInfo(StreamReader reader, out string title, out CubeFileDimension dim, out int size, out Vector3 min, out Vector3 max)
    {
        title = string.Empty;
        dim = CubeFileDimension.ThreeDimension;
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
                    if (lutSizeFound)
                        throw new Exception();
                    lutSizeFound = true;
                    Match match = s_lutSizeReg.Match(line);
                    size = int.Parse(match.Groups["size"].Value);
                    switch (match.Groups["dim"].Value)
                    {
                        case "3D":
                            if (size < 2 || 256 < size)
                                throw new Exception();
                            dim = CubeFileDimension.ThreeDimension;
                            break;

                        case "1D":
                            if (size < 2 || 65536 < size)
                                throw new Exception();
                            dim = CubeFileDimension.OneDimension;
                            break;

                        default:
                            throw new Exception();
                    }
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

    public bool Equals(CubeFile? other)
    {
        return ReferenceEquals(this, other);
    }

    public override bool Equals(object? obj)
    {
        return Equals(obj as CubeFile);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Title, Dimention, Size, Min, Max, Data);
    }
}
