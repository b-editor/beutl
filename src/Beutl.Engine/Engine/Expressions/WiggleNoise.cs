using Beutl.Graphics;

namespace Beutl.Engine.Expressions;

internal static class WiggleNoise
{
    private const int MaxOctaves = 16;
    private const ulong SplitMixIncrement = 0x9E3779B97F4A7C15UL;

    public static ulong CreateSeed(Guid ownerId, string propertyName)
    {
        // 64-bit FNV-1a over the big-endian GUID, then the UTF-8 name.
        Span<byte> id = stackalloc byte[16];
        ownerId.TryWriteBytes(id, bigEndian: true, out _);
        ulong hash = Fnv1a.Offset64;
        foreach (byte value in id)
            hash = Fnv1a.Append(hash, value);

        return Fnv1a.AppendUtf8(hash, propertyName);
    }

    public static double Sample(double x, int octaves, ulong seed)
    {
        if (!double.IsFinite(x))
            return double.NaN;
        if (octaves <= 0)
            return 0;

        double sum = 0;
        double weightSum = 0;
        double weight = 1;
        int octaveCount = Math.Min(octaves, MaxOctaves);

        for (int octave = 0; octave < octaveCount; octave++)
        {
            if (!double.IsFinite(x))
                return double.NaN;

            ulong octaveSeed = Mix(unchecked(seed + (ulong)octave * SplitMixIncrement));
            sum += PerlinSlice(x, octaveSeed) * weight;
            weightSum += weight;
            x *= 2;
            weight *= 0.5;
        }

        return Math.Clamp(sum / weightSum, -1.0, 1.0);
    }

    private static double PerlinSlice(double x, ulong seed)
    {
        double latticeX = Math.Floor(x);
        double offset = x - latticeX;
        double noise = SampleColumn(latticeX, offset, seed);
        if (offset != 0)
        {
            double right = SampleColumn(latticeX + 1, offset - 1, seed);
            double fade = offset * offset * offset * (offset * (6 * offset - 15) + 10);
            noise = noise * (1 - fade) + right * fade;
        }

        return Math.Clamp(noise * Math.Sqrt(2.0), -1.0, 1.0);
    }

    private static double SampleColumn(double latticeX, double dx, ulong seed)
    {
        // Hash the integer coordinate's double bits to cover the full finite range, including beyond Int64.
        ulong coordinate = latticeX == 0 ? 0 : BitConverter.DoubleToUInt64Bits(latticeX);
        ulong columnHash = Mix(coordinate ^ seed);

        // At y = 0.5 the quintic fade is 0.5, so the two corner contributions have equal weight.
        double lower = GradientDot(columnHash, dx, 0.5);
        double upper = GradientDot(Mix(columnHash), dx, -0.5);
        return (lower + upper) * 0.5;
    }

    private static double GradientDot(ulong hash, double dx, double dy)
    {
        // The half-step keeps the vector nonzero before normalization.
        double gx = unchecked((int)(hash >> 32)) + 0.5;
        double gy = unchecked((int)hash) + 0.5;
        return (gx * dx + gy * dy) / Math.Sqrt(gx * gx + gy * gy);
    }

    private static ulong Mix(ulong value)
    {
        unchecked
        {
            value += SplitMixIncrement;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
}
