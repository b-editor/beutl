using SkiaSharp;

namespace Beutl.Media;

// https://www.itu.int/epublications/publication/itu-t-h-273-v4-2024-07-coding-independent-code-points-for-video-signal-type-identification#:~:text=%C2%A0%C2%A0%C2%A0%C2%A0%C2%A0%C2%A0%C2%A0%C2%A0%C2%A0%C2%A0%20Colour%20primaries
public readonly struct BitmapColorSpaceXyz : IEquatable<BitmapColorSpaceXyz>
{
    private readonly float[] _values;

    private BitmapColorSpaceXyz(float[] values)
    {
        _values = values;
    }

    public float this[int row, int col] => _values[row * 3 + col];

    public ReadOnlySpan<float> Values => _values;

    /// <summary>sRGB / ITU-R BT.709</summary>
    public static BitmapColorSpaceXyz Srgb { get; } = FromSK(SKColorSpaceXyz.Srgb);

    public static BitmapColorSpaceXyz AdobeRgb { get; } = FromSK(SKColorSpaceXyz.AdobeRgb);

    /// <summary>SMPTE ST 432-1 / Display P3 (D65)</summary>
    public static BitmapColorSpaceXyz Dcip3 { get; } = FromSK(SKColorSpaceXyz.DisplayP3);

    /// <summary>ITU-R BT.2020</summary>
    public static BitmapColorSpaceXyz Rec2020 { get; } = FromSK(SKColorSpaceXyz.Rec2020);

    /// <summary>CIE 1931 XYZ / SMPTE ST 428-1</summary>
    public static BitmapColorSpaceXyz Xyz { get; } = FromSK(SKColorSpaceXyz.Xyz);

    /// <summary>BT.470M (NTSC 1953, Illuminant C)</summary>
    public static BitmapColorSpaceXyz Bt470M { get; } = FromPrimaries(
        0.67, 0.33,
        0.21, 0.71,
        0.14, 0.08,
        0.310, 0.316);

    /// <summary>BT.470BG (PAL/SECAM, D65)</summary>
    public static BitmapColorSpaceXyz Bt470BG { get; } = FromPrimaries(
        0.64, 0.33,
        0.29, 0.60,
        0.15, 0.06,
        0.3127, 0.3290);

    /// <summary>SMPTE 170M (NTSC, D65)</summary>
    public static BitmapColorSpaceXyz Smpte170M { get; } = FromPrimaries(
        0.630, 0.340,
        0.310, 0.595,
        0.155, 0.070,
        0.3127, 0.3290);

    /// <summary>SMPTE 240M (same primaries as SMPTE 170M)</summary>
    public static BitmapColorSpaceXyz Smpte240M => Smpte170M;

    /// <summary>Generic film (Illuminant C)</summary>
    public static BitmapColorSpaceXyz Film { get; } = FromPrimaries(
        0.681, 0.319,
        0.243, 0.692,
        0.145, 0.049,
        0.310, 0.316);

    /// <summary>SMPTE ST 431-2 / DCI-P3 (DCI white)</summary>
    public static BitmapColorSpaceXyz Smpte431 { get; } = FromPrimaries(
        0.680, 0.320,
        0.265, 0.690,
        0.150, 0.060,
        0.314, 0.351);

    /// <summary>EBU Tech. 3213-E / JEDEC P22 (D65)</summary>
    public static BitmapColorSpaceXyz Ebu3213 { get; } = FromPrimaries(
        0.630, 0.340,
        0.295, 0.605,
        0.155, 0.077,
        0.3127, 0.3290);

    /// <summary>ITU-R BT.709 / ITU-R BT1361 / IEC 61966-2-4 / SMPTE RP 177 Annex B</summary>
    public static BitmapColorSpaceXyz Bt709 { get; } = FromPrimaries(
        0.640, 0.330,
        0.300, 0.600,
        0.150, 0.060,
        0.3127, 0.3290);

    internal SKColorSpaceXyz ToSKXyz()
    {
        var v = _values;
        return new SKColorSpaceXyz(
            v[0], v[1], v[2],
            v[3], v[4], v[5],
            v[6], v[7], v[8]);
    }

    internal static BitmapColorSpaceXyz FromSK(SKColorSpaceXyz xyz)
    {
        return new BitmapColorSpaceXyz(xyz.Values);
    }

    public BitmapColorSpaceXyz Invert()
    {
        return FromSK(ToSKXyz().Invert());
    }

    public static BitmapColorSpaceXyz Concat(BitmapColorSpaceXyz a, BitmapColorSpaceXyz b)
    {
        return FromSK(SKColorSpaceXyz.Concat(a.ToSKXyz(), b.ToSKXyz()));
    }

    public bool Equals(BitmapColorSpaceXyz other)
    {
        if (_values is null && other._values is null) return true;
        if (_values is null || other._values is null) return false;
        return _values.AsSpan().SequenceEqual(other._values);
    }

    public override bool Equals(object? obj)
    {
        return obj is BitmapColorSpaceXyz other && Equals(other);
    }

    public override int GetHashCode()
    {
        if (_values is null) return 0;
        var hash = new HashCode();
        foreach (float v in _values)
            hash.Add(v);
        return hash.ToHashCode();
    }

    public static bool operator ==(BitmapColorSpaceXyz left, BitmapColorSpaceXyz right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(BitmapColorSpaceXyz left, BitmapColorSpaceXyz right)
    {
        return !left.Equals(right);
    }

    public override string ToString()
    {
        if (this == Srgb) return "sRGB";
        else if (this == AdobeRgb) return "Adobe RGB";
        else if (this == Dcip3) return "DCI-P3";
        else if (this == Rec2020) return "Rec.2020";
        else if (this == Xyz) return "XYZ";
        else if (this == Bt470M) return "BT.470M";
        else if (this == Bt470BG) return "BT.470BG";
        else if (this == Smpte170M) return "SMPTE 170M";
        else if (this == Film) return "Film";
        else if (this == Smpte431) return "SMPTE ST 431";
        else if (this == Ebu3213) return "EBU 3213";
        else if (this == Bt709) return "BT.709";
        else return $"Custom({string.Join(", ", _values)})";
    }

    // CIE xy色度座標とホワイトポイントからtoXYZD50行列を計算する。
    // Bradford色順応変換を使用してD50に適応させる。
    private static BitmapColorSpaceXyz FromPrimaries(
        double xr, double yr, double xg, double yg, double xb, double yb,
        double xw, double yw)
    {
        // 各原色のXYZ座標を計算
        double Xr = xr / yr, Yr = 1.0, Zr = (1.0 - xr - yr) / yr;
        double Xg = xg / yg, Yg = 1.0, Zg = (1.0 - xg - yg) / yg;
        double Xb = xb / yb, Yb = 1.0, Zb = (1.0 - xb - yb) / yb;

        // ホワイトポイントのXYZ
        double Xw = xw / yw, Yw = 1.0, Zw = (1.0 - xw - yw) / yw;

        // 原色行列 M = [[Xr,Xg,Xb],[Yr,Yg,Yb],[Zr,Zg,Zb]] の逆行列でスケーリング係数を求める
        double det = Xr * (Yg * Zb - Yb * Zg)
                   - Xg * (Yr * Zb - Yb * Zr)
                   + Xb * (Yr * Zg - Yg * Zr);
        double invDet = 1.0 / det;

        double Sr = ((Yg * Zb - Yb * Zg) * Xw + (Xb * Zg - Xg * Zb) * Yw + (Xg * Yb - Xb * Yg) * Zw) * invDet;
        double Sg = ((Yb * Zr - Yr * Zb) * Xw + (Xr * Zb - Xb * Zr) * Yw + (Xb * Yr - Xr * Yb) * Zw) * invDet;
        double Sb = ((Yr * Zg - Yg * Zr) * Xw + (Xg * Zr - Xr * Zg) * Yw + (Xr * Yg - Xg * Yr) * Zw) * invDet;

        // toXYZ行列 (ネイティブホワイト基準)
        double t00 = Xr * Sr, t01 = Xg * Sg, t02 = Xb * Sb;
        double t10 = Yr * Sr, t11 = Yg * Sg, t12 = Yb * Sb;
        double t20 = Zr * Sr, t21 = Zg * Sg, t22 = Zb * Sb;

        // Bradford色順応変換: ソースホワイト → D50
        const double d50X = 0.96422, d50Y = 1.0, d50Z = 0.82521;

        // Bradford行列
        const double b00 = 0.8951, b01 = 0.2664, b02 = -0.1614;
        const double b10 = -0.7502, b11 = 1.7135, b12 = 0.0367;
        const double b20 = 0.0389, b21 = -0.0685, b22 = 1.0296;

        // Bradford逆行列
        const double bi00 = 0.9869929, bi01 = -0.1470543, bi02 = 0.1599627;
        const double bi10 = 0.4323053, bi11 = 0.5183603, bi12 = 0.0492912;
        const double bi20 = -0.0085287, bi21 = 0.0400428, bi22 = 0.9684867;

        // コーン応答空間でのソースホワイト
        double coneS0 = b00 * Xw + b01 * Yw + b02 * Zw;
        double coneS1 = b10 * Xw + b11 * Yw + b12 * Zw;
        double coneS2 = b20 * Xw + b21 * Yw + b22 * Zw;

        // コーン応答空間でのD50
        double coneD0 = b00 * d50X + b01 * d50Y + b02 * d50Z;
        double coneD1 = b10 * d50X + b11 * d50Y + b12 * d50Z;
        double coneD2 = b20 * d50X + b21 * d50Y + b22 * d50Z;

        // スケーリング
        double s0 = coneD0 / coneS0;
        double s1 = coneD1 / coneS1;
        double s2 = coneD2 / coneS2;

        // 適応行列 = Bradford_inv * diag(s) * Bradford
        double ds00 = s0 * b00, ds01 = s0 * b01, ds02 = s0 * b02;
        double ds10 = s1 * b10, ds11 = s1 * b11, ds12 = s1 * b12;
        double ds20 = s2 * b20, ds21 = s2 * b21, ds22 = s2 * b22;

        double a00 = bi00 * ds00 + bi01 * ds10 + bi02 * ds20;
        double a01 = bi00 * ds01 + bi01 * ds11 + bi02 * ds21;
        double a02 = bi00 * ds02 + bi01 * ds12 + bi02 * ds22;
        double a10 = bi10 * ds00 + bi11 * ds10 + bi12 * ds20;
        double a11 = bi10 * ds01 + bi11 * ds11 + bi12 * ds21;
        double a12 = bi10 * ds02 + bi11 * ds12 + bi12 * ds22;
        double a20 = bi20 * ds00 + bi21 * ds10 + bi22 * ds20;
        double a21 = bi20 * ds01 + bi21 * ds11 + bi22 * ds21;
        double a22 = bi20 * ds02 + bi21 * ds12 + bi22 * ds22;

        // toXYZD50 = Adaptation * toXYZ
        return new BitmapColorSpaceXyz(
        [
            (float)(a00 * t00 + a01 * t10 + a02 * t20),
            (float)(a00 * t01 + a01 * t11 + a02 * t21),
            (float)(a00 * t02 + a01 * t12 + a02 * t22),
            (float)(a10 * t00 + a11 * t10 + a12 * t20),
            (float)(a10 * t01 + a11 * t11 + a12 * t21),
            (float)(a10 * t02 + a11 * t12 + a12 * t22),
            (float)(a20 * t00 + a21 * t10 + a22 * t20),
            (float)(a20 * t01 + a21 * t11 + a22 * t21),
            (float)(a20 * t02 + a21 * t12 + a22 * t22),
        ]);
    }
}
