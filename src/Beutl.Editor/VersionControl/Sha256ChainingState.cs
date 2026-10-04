using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;

namespace Beutl.Editor.VersionControl;

/// <summary>
/// SHA-256 whose chaining state can be read between whole blocks, which .NET's own implementation
/// does not expose. Hosted Git takes parallel LFS parts that each name the state of the bytes
/// before them, and checks every named state against the bytes it received.
/// </summary>
internal sealed class Sha256ChainingState
{
    private const int BlockSize = 64;
    private static readonly SearchValues<char> s_lowerHex = SearchValues.Create("0123456789abcdef");

    private static ReadOnlySpan<uint> K =>
    [
        0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
        0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
        0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
        0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
        0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
        0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
        0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
        0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
    ];

    private readonly uint[] _state = [0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19];
    private readonly uint[] _schedule = new uint[64];

    /// <summary>The number of bytes hashed so far, always a whole number of blocks.</summary>
    public long Length { get; private set; }

    /// <summary>Continues from a state hosted Git reported for the first <paramref name="length"/> bytes.</summary>
    public static Sha256ChainingState Resume(string state, long length)
    {
        if (state.Length != 64 || state.AsSpan().ContainsAnyExcept(s_lowerHex) || length < 0 || length % BlockSize != 0)
            throw new FormatException("Invalid SHA-256 chaining state");
        var resumed = new Sha256ChainingState { Length = length };
        for (int i = 0; i < 8; i++)
            resumed._state[i] = uint.Parse(state.AsSpan(i * 8, 8), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        return resumed;
    }

    /// <summary>The eight chaining words as 64 lowercase hex digits, the form hosted Git exchanges.</summary>
    public override string ToString()
    {
        Span<byte> bytes = stackalloc byte[32];
        for (int i = 0; i < 8; i++) BinaryPrimitives.WriteUInt32BigEndian(bytes[(i * 4)..], _state[i]);
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>Hashes whole blocks; the length must be a multiple of 64 bytes.</summary>
    public void Append(ReadOnlySpan<byte> blocks)
    {
        if (blocks.Length % BlockSize != 0) throw new ArgumentException("SHA-256 input must be whole blocks", nameof(blocks));
        for (int offset = 0; offset < blocks.Length; offset += BlockSize) Compress(blocks.Slice(offset, BlockSize));
        Length += blocks.Length;
    }

    /// <summary>Hashes the final bytes and returns the digest as lowercase hex; the state cannot be used afterwards.</summary>
    public string Finish(ReadOnlySpan<byte> rest)
    {
        int whole = rest.Length - rest.Length % BlockSize;
        Append(rest[..whole]);
        ReadOnlySpan<byte> tail = rest[whole..];
        Span<byte> padding = stackalloc byte[2 * BlockSize];
        padding.Clear();
        tail.CopyTo(padding);
        padding[tail.Length] = 0x80;
        int blocks = tail.Length < BlockSize - 8 ? 1 : 2;
        BinaryPrimitives.WriteUInt64BigEndian(padding[(blocks * BlockSize - 8)..], (ulong)(Length + tail.Length) * 8);
        for (int i = 0; i < blocks; i++) Compress(padding.Slice(i * BlockSize, BlockSize));
        return ToString();
    }

    private void Compress(ReadOnlySpan<byte> block)
    {
        Span<uint> w = _schedule;
        for (int i = 0; i < 16; i++) w[i] = BinaryPrimitives.ReadUInt32BigEndian(block[(i * 4)..]);
        for (int i = 16; i < 64; i++)
        {
            uint s0 = BitOperations.RotateRight(w[i - 15], 7) ^ BitOperations.RotateRight(w[i - 15], 18) ^ (w[i - 15] >> 3);
            uint s1 = BitOperations.RotateRight(w[i - 2], 17) ^ BitOperations.RotateRight(w[i - 2], 19) ^ (w[i - 2] >> 10);
            w[i] = w[i - 16] + s0 + w[i - 7] + s1;
        }

        uint a = _state[0], b = _state[1], c = _state[2], d = _state[3];
        uint e = _state[4], f = _state[5], g = _state[6], h = _state[7];
        ReadOnlySpan<uint> k = K;
        for (int i = 0; i < 64; i++)
        {
            uint t1 = h + (BitOperations.RotateRight(e, 6) ^ BitOperations.RotateRight(e, 11) ^ BitOperations.RotateRight(e, 25))
                + ((e & f) ^ (~e & g)) + k[i] + w[i];
            uint t2 = (BitOperations.RotateRight(a, 2) ^ BitOperations.RotateRight(a, 13) ^ BitOperations.RotateRight(a, 22))
                + ((a & b) ^ (a & c) ^ (b & c));
            h = g;
            g = f;
            f = e;
            e = d + t1;
            d = c;
            c = b;
            b = a;
            a = t1 + t2;
        }

        _state[0] += a;
        _state[1] += b;
        _state[2] += c;
        _state[3] += d;
        _state[4] += e;
        _state[5] += f;
        _state[6] += g;
        _state[7] += h;
    }
}
