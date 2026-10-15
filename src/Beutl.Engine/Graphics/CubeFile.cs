using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Beutl.Graphics;

public partial class CubeFile : IEquatable<CubeFile>
{
    private static readonly Regex s_lutSizeReg = LUTSizeRegex();
    private static readonly Regex s_titleReg = TitleRegex();
    private static readonly Regex s_domainMinReg = DomainMinRegex();
    private static readonly Regex s_domainMaxReg = DomainMaxRegex();

    public required CubeFileDimension Dimention { get; init; }

    public required int Size { get; init; }

    public required Vector3 Min { get; init; }

    public required Vector3 Max { get; init; }

    public required Vector3[] Data { get; init; }

    public static CubeFile FromStream(Stream stream)
    {
        using var reader = new StreamReader(stream);
        int i = 0;
        ReadInfo(
            reader,
            out CubeFileDimension dim,
            out int size,
            out Vector3 min,
            out Vector3 max);

        int length = (int)Math.Pow(size, (int)dim);
        Vector3[] data = new Vector3[length];

        while (i < length)
        {
            string? line = reader.ReadLine();
            if (line == null)
                throw new InvalidDataException("The LUT does not contain the declared number of entries.");
            if (!line.StartsWith('#'))
            {
                string[] values = line.Split(' ');
                if (values.Length != 3) continue;

                if (float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float r) &&
                    float.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float g) &&
                    float.TryParse(values[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float b))
                {
                    data[i] = new(r, g, b);
                    i++;
                }
            }
        }

        return new CubeFile
        {
            Dimention = dim,
            Size = size,
            Min = min,
            Max = max,
            Data = data
        };
    }

    private static void ReadInfo(StreamReader reader, out CubeFileDimension dim, out int size, out Vector3 min, out Vector3 max)
    {
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
                    size = int.Parse(match.Groups["size"].Value, CultureInfo.InvariantCulture);
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
                    // The title is not kept, but it still counts towards the header lines the scan stops after.
                    titleFound = true;
                }
                else if (s_domainMaxReg.IsMatch(line))
                {
                    maxFound = true;
                    max = ParseDomain(s_domainMaxReg.Match(line));
                }
                else if (s_domainMinReg.IsMatch(line))
                {
                    minFound = true;
                    min = ParseDomain(s_domainMinReg.Match(line));
                }
            }
        }

        if (!lutSizeFound)
            throw new InvalidDataException("The file does not declare a LUT size.");
        reader.BaseStream.Position = 0;
        reader.DiscardBufferedData();
    }

    /// <summary>Reads the red, green and blue bounds a <c>DOMAIN_MIN</c> or <c>DOMAIN_MAX</c> line declares.</summary>
    private static Vector3 ParseDomain(Match match)
    {
        float r = float.Parse(match.Groups["red"].Value, CultureInfo.InvariantCulture);
        float g = float.Parse(match.Groups["green"].Value, CultureInfo.InvariantCulture);
        float b = float.Parse(match.Groups["blue"].Value, CultureInfo.InvariantCulture);
        return new(r, g, b);
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
        return HashCode.Combine(Dimention, Size, Min, Max, Data);
    }
}
