using System.Text;

namespace Beutl.Evidence;

/// <summary>Provides xoshiro256** resampling seeded through SplitMix64.</summary>
/// <remarks>
/// A fixed algorithm keeps confidence intervals reproducible across runtime versions.
/// </remarks>
public sealed class DeterministicBootstrapRandom
{
    private ulong _s0;
    private ulong _s1;
    private ulong _s2;
    private ulong _s3;

    public DeterministicBootstrapRandom(uint seed)
    {
        ulong state = seed;
        _s0 = SplitMix64(ref state);
        _s1 = SplitMix64(ref state);
        _s2 = SplitMix64(ref state);
        _s3 = SplitMix64(ref state);
    }

    /// <summary>The FNV-1a 32-bit hash of <paramref name="value"/>'s UTF-8 bytes.</summary>
    public static uint Fnv1a32(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        uint hash = 2166136261u;
        foreach (byte item in Encoding.UTF8.GetBytes(value))
        {
            hash ^= item;
            hash *= 16777619u;
        }

        return hash;
    }

    /// <summary>Combines the SC-008 base seed with a case name so every case resamples independently.</summary>
    public static uint DeriveSeed(int baseSeed, string caseName)
        => unchecked((uint)baseSeed) ^ Fnv1a32(caseName);

    /// <summary>A uniform integer in <c>[0, exclusiveUpperBound)</c>, using Lemire's debiased multiply-shift.</summary>
    public int NextIndex(int exclusiveUpperBound)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveUpperBound);
        uint bound = (uint)exclusiveUpperBound;
        ulong product = (ulong)NextUInt32() * bound;
        uint low = (uint)product;
        if (low < bound)
        {
            uint threshold = (0u - bound) % bound;
            while (low < threshold)
            {
                product = (ulong)NextUInt32() * bound;
                low = (uint)product;
            }
        }

        return (int)(product >> 32);
    }

    private uint NextUInt32() => (uint)(NextUInt64() >> 32);

    private ulong NextUInt64()
    {
        ulong result = System.Numerics.BitOperations.RotateLeft(unchecked(_s1 * 5), 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = System.Numerics.BitOperations.RotateLeft(_s3, 45);
        return result;
    }

    private static ulong SplitMix64(ref ulong state)
    {
        unchecked
        {
            state += 0x9E3779B97F4A7C15ul;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
            return z ^ (z >> 31);
        }
    }
}
