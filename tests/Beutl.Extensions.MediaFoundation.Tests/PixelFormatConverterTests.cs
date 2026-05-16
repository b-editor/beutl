using System;
using System.Runtime.InteropServices;
using Beutl.Embedding.MediaFoundation;

namespace Beutl.Extensions.MediaFoundation.Tests;

// See MFColorSpaceHelperTests for why these run on Windows only.
[Platform("Win")]
[TestFixture]
public unsafe class PixelFormatConverterTests
{
    private static byte[] AllocateBgra(int width, int height, byte b, byte g, byte r, byte a = 255)
    {
        int stride = width * 4;
        var buffer = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * stride + x * 4;
                buffer[i + 0] = b;
                buffer[i + 1] = g;
                buffer[i + 2] = r;
                buffer[i + 3] = a;
            }
        }
        return buffer;
    }

    private static byte[] AllocateNv12Buffer(int width, int height)
    {
        int dstStride = ((width + 1) / 2) * 2;
        int uvHeight = (height + 1) / 2;
        return new byte[dstStride * (height + uvHeight)];
    }

    [Test]
    public void BgraToNv12_RejectsNegativeDimensions()
    {
        byte[] src = new byte[16];
        byte[] dst = new byte[16];
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            fixed (byte* s = src)
            fixed (byte* d = dst)
            {
                PixelFormatConverter.BgraToNv12(s, 16, d, 8, -1, 2, PixelFormatConverter.YuvMatrix8.Bt709);
            }
        });
    }

    [Test]
    public void BgraToNv12_RejectsTooSmallSrcStride()
    {
        byte[] src = new byte[8];
        byte[] dst = new byte[16];
        Assert.Throws<ArgumentException>(() =>
        {
            fixed (byte* s = src)
            fixed (byte* d = dst)
            {
                PixelFormatConverter.BgraToNv12(s, 4, d, 8, 4, 1, PixelFormatConverter.YuvMatrix8.Bt709);
            }
        });
    }

    [Test]
    public void BgraToNv12_RejectsTooSmallDstStride()
    {
        // Width 5 needs chroma stride ceil(5/2)*2 = 6 bytes.
        byte[] src = new byte[40];
        byte[] dst = new byte[16];
        Assert.Throws<ArgumentException>(() =>
        {
            fixed (byte* s = src)
            fixed (byte* d = dst)
            {
                PixelFormatConverter.BgraToNv12(s, 20, d, 4, 5, 1, PixelFormatConverter.YuvMatrix8.Bt709);
            }
        });
    }

    [Test]
    public void BgraToNv12_AcceptsOddWidth_NoBufferOverrun()
    {
        // width=5 → chromaWidth=6. The Y plane writes only 5 bytes per row;
        // the trailing padding byte is left for the caller (encoder zeros it).
        // We simply require this not to throw or write outside the buffer.
        const int width = 5;
        const int height = 4;
        byte[] src = AllocateBgra(width, height, 200, 100, 50);
        byte[] dst = AllocateNv12Buffer(width, height);

        Assert.DoesNotThrow(() =>
        {
            fixed (byte* s = src)
            fixed (byte* d = dst)
            {
                PixelFormatConverter.BgraToNv12(s, width * 4, d, 6, width, height,
                    PixelFormatConverter.YuvMatrix8.Bt709);
            }
        });
    }

    [Test]
    public void BgraToNv12_RoundTrip_PreservesGreyscale()
    {
        // Greyscale (R=G=B) means Cb=Cr=128 (centered). Going through limited-range
        // BT.709 forward + inverse should keep |Δ| well within a few code values.
        const int width = 16;
        const int height = 8;
        byte[] src = AllocateBgra(width, height, 128, 128, 128);
        byte[] nv12 = AllocateNv12Buffer(width, height);
        byte[] roundTrip = new byte[width * 4 * height];

        fixed (byte* s = src)
        fixed (byte* d = nv12)
        fixed (byte* r = roundTrip)
        {
            PixelFormatConverter.BgraToNv12(s, width * 4, d, width, width, height,
                PixelFormatConverter.YuvMatrix8.Bt709);
            PixelFormatConverter.Nv12ToBgra(d, width, r, width * 4, width, height,
                PixelFormatConverter.InvYuvMatrix8.Bt709);
        }

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = y * width * 4 + x * 4;
                Assert.That(Math.Abs(roundTrip[i + 0] - 128), Is.LessThanOrEqualTo(2), $"B at ({x},{y})");
                Assert.That(Math.Abs(roundTrip[i + 1] - 128), Is.LessThanOrEqualTo(2), $"G at ({x},{y})");
                Assert.That(Math.Abs(roundTrip[i + 2] - 128), Is.LessThanOrEqualTo(2), $"R at ({x},{y})");
                Assert.That(roundTrip[i + 3], Is.EqualTo(255));
            }
        }
    }

    private static int MaxChannelDiff(
        PixelFormatConverter.YuvMatrix8 forward,
        PixelFormatConverter.InvYuvMatrix8 inverse)
    {
        const int width = 16;
        const int height = 8;

        var cases = new[]
        {
            (B: (byte)0, G: (byte)0, R: (byte)255),
            (B: (byte)0, G: (byte)255, R: (byte)0),
            (B: (byte)255, G: (byte)0, R: (byte)0),
            (B: (byte)255, G: (byte)255, R: (byte)255),
            (B: (byte)0, G: (byte)0, R: (byte)0),
        };

        int worst = 0;
        foreach (var (b, g, r) in cases)
        {
            byte[] src = AllocateBgra(width, height, b, g, r);
            byte[] nv12 = AllocateNv12Buffer(width, height);
            byte[] roundTrip = new byte[width * 4 * height];

            fixed (byte* sp = src)
            fixed (byte* dp = nv12)
            fixed (byte* rp = roundTrip)
            {
                PixelFormatConverter.BgraToNv12(sp, width * 4, dp, width, width, height, forward);
                PixelFormatConverter.Nv12ToBgra(dp, width, rp, width * 4, width, height, inverse);
            }

            int i = (height / 2) * width * 4 + (width / 2) * 4;
            worst = Math.Max(worst, Math.Abs(roundTrip[i + 0] - b));
            worst = Math.Max(worst, Math.Abs(roundTrip[i + 1] - g));
            worst = Math.Max(worst, Math.Abs(roundTrip[i + 2] - r));
        }
        return worst;
    }

    private static void AssertPrimariesRoundTrip(
        PixelFormatConverter.YuvMatrix8 forward,
        PixelFormatConverter.InvYuvMatrix8 inverse,
        string matrixName,
        int tolerance = 5)
    {
        // Tightened from the original ±10 LSB to ±5 once the corrected SMPTE 240M
        // inverse stopped grazing the limit. A drift beyond 5 LSB on saturated
        // primaries indicates the forward/inverse pair lost symmetry — e.g. an
        // accidentally swapped coefficient or the inverse not matching the
        // matrix tag the encoder advertises.
        int worst = MaxChannelDiff(forward, inverse);
        Assert.That(worst, Is.LessThanOrEqualTo(tolerance),
            $"{matrixName}: round-trip drift {worst} LSB exceeds tolerance {tolerance}");
    }

    [Test]
    public void BgraToNv12_RoundTrip_Bt601()
    {
        AssertPrimariesRoundTrip(
            PixelFormatConverter.YuvMatrix8.Bt601,
            PixelFormatConverter.InvYuvMatrix8.Bt601,
            "BT.601");
    }

    [Test]
    public void BgraToNv12_RoundTrip_Bt2020()
    {
        AssertPrimariesRoundTrip(
            PixelFormatConverter.YuvMatrix8.Bt2020,
            PixelFormatConverter.InvYuvMatrix8.Bt2020,
            "BT.2020");
    }

    [Test]
    public void BgraToNv12_RoundTrip_Smpte240M()
    {
        // SMPTE 240M previously had asymmetric inverse coefficients, so saturated
        // primaries drifted visibly. With the corrected inverse the round-trip
        // fits inside the ±5 LSB envelope this helper enforces.
        AssertPrimariesRoundTrip(
            PixelFormatConverter.YuvMatrix8.Smpte240M,
            PixelFormatConverter.InvYuvMatrix8.Smpte240M,
            "SMPTE 240M");
    }

    [Test]
    public void Yuy2ToBgra_RejectsTooSmallDstStride()
    {
        byte[] src = new byte[8];
        byte[] dst = new byte[8];
        Assert.Throws<ArgumentException>(() =>
        {
            fixed (byte* s = src)
            fixed (byte* d = dst)
            {
                PixelFormatConverter.Yuy2ToBgra(s, d, 4, 2, 1,
                    PixelFormatConverter.InvYuvMatrix8.Bt709);
            }
        });
    }

    [Test]
    public void Yuy2ToBgra_GreyscaleRoundTripWithinTolerance()
    {
        // Synthesize a uniform grey YUY2 frame (Y=128, Cb=Cr=128) and verify
        // the inverse maps it back to ~128 RGB across BT.709, BT.601, BT.2020,
        // and SMPTE 240M. A wrong matrix here would show up as a non-grey tint.
        const int width = 8;
        const int height = 4;
        int srcSize = width * height * 2;
        byte[] src = new byte[srcSize];
        for (int i = 0; i < srcSize; i += 4)
        {
            src[i + 0] = 128; // Y0
            src[i + 1] = 128; // Cb
            src[i + 2] = 128; // Y1
            src[i + 3] = 128; // Cr
        }
        byte[] dst = new byte[width * 4 * height];

        var matrices = new[]
        {
            ("Bt709", PixelFormatConverter.InvYuvMatrix8.Bt709),
            ("Bt601", PixelFormatConverter.InvYuvMatrix8.Bt601),
            ("Bt2020", PixelFormatConverter.InvYuvMatrix8.Bt2020),
            ("Smpte240M", PixelFormatConverter.InvYuvMatrix8.Smpte240M),
        };

        foreach (var (name, matrix) in matrices)
        {
            Array.Clear(dst, 0, dst.Length);
            fixed (byte* sp = src)
            fixed (byte* dp = dst)
            {
                PixelFormatConverter.Yuy2ToBgra(sp, dp, width * 4, width, height, matrix);
            }

            int i = (height / 2) * width * 4 + (width / 2) * 4;
            // Limited-range Y=128 maps to ~130 RGB after the 16..235 → 0..255
            // expansion (298·(128−16)/256 ≈ 130). Tight tolerance to catch
            // any matrix coefficient that accidentally affects the grey axis.
            Assert.That(Math.Abs(dst[i + 0] - 130), Is.LessThanOrEqualTo(3),
                $"{name}: B drifted from neutral grey to {dst[i + 0]}");
            Assert.That(Math.Abs(dst[i + 1] - 130), Is.LessThanOrEqualTo(3),
                $"{name}: G drifted from neutral grey to {dst[i + 1]}");
            Assert.That(Math.Abs(dst[i + 2] - 130), Is.LessThanOrEqualTo(3),
                $"{name}: R drifted from neutral grey to {dst[i + 2]}");
            Assert.That(dst[i + 3], Is.EqualTo(255));
        }
    }

    [Test]
    public void Smpte240M_PreCorrectionInverseFailsTightTolerance()
    {
        // Regression guard: synthesize the legacy SMPTE 240M inverse and assert it
        // drifts beyond the tight tolerance. If someone re-introduces those
        // coefficients (or the round-trip becomes lax enough to mask them), this
        // test fails before the new correctness tests do.
        var legacyInverse = new PixelFormatConverter.InvYuvMatrix8(
            crToR: 451, cbToG: 56, crToG: 138, cbToB: 535);
        int worst = MaxChannelDiff(
            PixelFormatConverter.YuvMatrix8.Smpte240M, legacyInverse);
        Assert.That(worst, Is.GreaterThan(5),
            $"Legacy SMPTE 240M inverse should drift past ±5 LSB; observed {worst}");
    }

    [Test]
    public void BgraToNv12_RoundTrip_PreservesPrimaryColorsBt709()
    {
        // Saturated primaries through BT.709 round-trip — accept ~8 LSB error from
        // chroma subsampling + limited-range quantization. The test catches gross
        // matrix mismatches (e.g. using BT.601 coefficients in the inverse).
        const int width = 16;
        const int height = 8;

        var cases = new[]
        {
            (B: (byte)0, G: (byte)0, R: (byte)255), // red
            (B: (byte)0, G: (byte)255, R: (byte)0), // green
            (B: (byte)255, G: (byte)0, R: (byte)0), // blue
            (B: (byte)255, G: (byte)255, R: (byte)255), // white
            (B: (byte)0, G: (byte)0, R: (byte)0), // black
        };

        foreach (var (b, g, r) in cases)
        {
            byte[] src = AllocateBgra(width, height, b, g, r);
            byte[] nv12 = AllocateNv12Buffer(width, height);
            byte[] roundTrip = new byte[width * 4 * height];

            fixed (byte* sp = src)
            fixed (byte* dp = nv12)
            fixed (byte* rp = roundTrip)
            {
                PixelFormatConverter.BgraToNv12(sp, width * 4, dp, width, width, height,
                    PixelFormatConverter.YuvMatrix8.Bt709);
                PixelFormatConverter.Nv12ToBgra(dp, width, rp, width * 4, width, height,
                    PixelFormatConverter.InvYuvMatrix8.Bt709);
            }

            // Sample the middle pixel — corners may suffer extra chroma error from
            // the (width+1)/2 boundary handling.
            int i = (height / 2) * width * 4 + (width / 2) * 4;
            Assert.That(Math.Abs(roundTrip[i + 0] - b), Is.LessThanOrEqualTo(8),
                $"B for ({r},{g},{b}): got {roundTrip[i + 0]}");
            Assert.That(Math.Abs(roundTrip[i + 1] - g), Is.LessThanOrEqualTo(8),
                $"G for ({r},{g},{b}): got {roundTrip[i + 1]}");
            Assert.That(Math.Abs(roundTrip[i + 2] - r), Is.LessThanOrEqualTo(8),
                $"R for ({r},{g},{b}): got {roundTrip[i + 2]}");
        }
    }

    [Test]
    public void Rgba16ToP010_RejectsNonEvenStride()
    {
        byte[] src = new byte[64];
        byte[] dst = new byte[64];
        Assert.Throws<ArgumentException>(() =>
        {
            fixed (byte* s = src)
            fixed (byte* d = dst)
            {
                // odd dstStride is invalid for P010
                PixelFormatConverter.Rgba16ToP010(s, 16, d, 5, 2, 2);
            }
        });
    }

    [Test]
    public void Rgba16ToP010_RejectsTooSmallStride()
    {
        byte[] src = new byte[64];
        byte[] dst = new byte[64];
        Assert.Throws<ArgumentException>(() =>
        {
            fixed (byte* s = src)
            fixed (byte* d = dst)
            {
                // width=5 → chroma row needs ceil(5/2)*4 = 12 bytes
                PixelFormatConverter.Rgba16ToP010(s, 40, d, 4, 5, 2);
            }
        });
    }

    [Test]
    public void Rgba16ToP010_StoresMsbAlignedPackedSamples()
    {
        // P010 packs 10-bit codes in the high 10 bits of each ushort, so every
        // sample should have its low 6 bits clear after conversion.
        const int width = 8;
        const int height = 4;
        var src = new ushort[width * 4 * height];
        for (int i = 0; i < src.Length; i += 4)
        {
            src[i + 0] = 32768; // R
            src[i + 1] = 16384; // G
            src[i + 2] = 8192;  // B
            src[i + 3] = 65535; // A
        }
        int dstStride = ((width + 1) / 2) * 4;
        int uvHeight = (height + 1) / 2;
        byte[] dst = new byte[dstStride * (height + uvHeight)];

        fixed (ushort* sp = src)
        fixed (byte* dp = dst)
        {
            PixelFormatConverter.Rgba16ToP010((byte*)sp, width * 8, dp, dstStride, width, height);
        }

        var dstAsShorts = MemoryMarshal.Cast<byte, ushort>(dst);
        foreach (ushort sample in dstAsShorts)
        {
            Assert.That(sample & 0x3F, Is.Zero, $"Sample {sample:X4} not MSB-aligned");
        }
    }

    [Test]
    public void Rgba16ToP010_AcceptsOddWidth()
    {
        const int width = 5;
        const int height = 4;
        int srcStride = width * 8;
        byte[] src = new byte[srcStride * height];
        int dstStride = ((width + 1) / 2) * 4;
        int uvHeight = (height + 1) / 2;
        byte[] dst = new byte[dstStride * (height + uvHeight)];

        Assert.DoesNotThrow(() =>
        {
            fixed (byte* s = src)
            fixed (byte* d = dst)
            {
                PixelFormatConverter.Rgba16ToP010(s, srcStride, d, dstStride, width, height);
            }
        });
    }

    [Test]
    public void YuvMatrix8_PresetsAreNonZero()
    {
        // Cheap smoke test that the presets weren't accidentally zeroed by a bad
        // edit — a zero matrix would crash all encoding without surfacing a clear
        // error.
        Assert.That(PixelFormatConverter.YuvMatrix8.Bt709.Yg, Is.GreaterThan(0));
        Assert.That(PixelFormatConverter.YuvMatrix8.Bt601.Yg, Is.GreaterThan(0));
        Assert.That(PixelFormatConverter.YuvMatrix8.Bt2020.Yg, Is.GreaterThan(0));
        Assert.That(PixelFormatConverter.YuvMatrix8.Smpte240M.Yg, Is.GreaterThan(0));
    }

    [Test]
    public void YuvMatrix8_LumaCoefficientsSumNearQ8Unity()
    {
        // Each forward matrix should have Yr + Yg + Yb ≈ 256 (Q8 unity gain).
        // Off-by-one rounding is fine; deviation > 2 indicates a coefficient bug.
        AssertLumaSum(PixelFormatConverter.YuvMatrix8.Bt709, "Bt709");
        AssertLumaSum(PixelFormatConverter.YuvMatrix8.Bt601, "Bt601");
        AssertLumaSum(PixelFormatConverter.YuvMatrix8.Bt2020, "Bt2020");
        AssertLumaSum(PixelFormatConverter.YuvMatrix8.Smpte240M, "Smpte240M");

        static void AssertLumaSum(PixelFormatConverter.YuvMatrix8 m, string name)
        {
            int sum = m.Yr + m.Yg + m.Yb;
            Assert.That(Math.Abs(sum - 256), Is.LessThanOrEqualTo(2),
                $"{name}: Yr+Yg+Yb = {sum}, expected ~256");
        }
    }
}
