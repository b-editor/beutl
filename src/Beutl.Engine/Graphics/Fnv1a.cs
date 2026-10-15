namespace Beutl.Graphics;

/// <summary>The FNV-1a hash behind the engine's stable digests, which must not vary between processes.</summary>
internal static class Fnv1a
{
    public const ulong Offset64 = 14695981039346656037UL;
    public const uint Offset32 = 2166136261;
    private const ulong Prime64 = 1099511628211UL;
    private const uint Prime32 = 16777619;

    /// <summary>Folds <paramref name="value"/> in as one unit rather than byte by byte.</summary>
    public static ulong Append(ulong hash, ulong value)
        => unchecked((hash ^ value) * Prime64);

    /// <inheritdoc cref="Append(ulong, ulong)"/>
    public static uint Append(uint hash, uint value)
        => unchecked((hash ^ value) * Prime32);

    /// <summary>Folds the eight bytes of <paramref name="value"/> in, least significant first.</summary>
    public static ulong AppendBytes(ulong hash, ulong value)
    {
        for (int shift = 0; shift < 64; shift += 8)
            hash = Append(hash, (value >> shift) & 0xFF);

        return hash;
    }

    /// <summary>Folds each UTF-16 code unit of <paramref name="text"/> in as one unit.</summary>
    public static uint AppendChars(uint hash, string text)
    {
        foreach (char value in text)
            hash = Append(hash, value);

        return hash;
    }
}
