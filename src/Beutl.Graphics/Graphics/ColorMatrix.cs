using System.Globalization;
using System.Runtime.InteropServices;

namespace Beutl.Graphics;

[StructLayout(LayoutKind.Sequential)]
public readonly struct ColorMatrix : IEquatable<ColorMatrix>
{
    public ColorMatrix(
        float m00, float m01, float m02, float m03, float m04,
        float m10, float m11, float m12, float m13, float m14,
        float m20, float m21, float m22, float m23, float m24,
        float m30, float m31, float m32, float m33, float m34)
    {
        M00 = m00;
        M01 = m01;
        M02 = m02;
        M03 = m03;
        M04 = m04;

        M10 = m10;
        M11 = m11;
        M12 = m12;
        M13 = m13;
        M14 = m14;

        M20 = m20;
        M21 = m21;
        M22 = m22;
        M23 = m23;
        M24 = m24;

        M30 = m30;
        M31 = m31;
        M32 = m32;
        M33 = m33;
        M34 = m34;
    }

    public static ColorMatrix Identity { get; } =
        new ColorMatrix(
            1F, 0F, 0F, 0F, 0F,
            0F, 1F, 0F, 0F, 0F,
            0F, 0F, 1F, 0F, 0F,
            0F, 0F, 0F, 1F, 0F);

    public bool IsIdentity
    {
        get
        {
            return
                M00 == 1F && M01 == 0F && M02 == 0F && M03 == 0F && M04 == 0F &&
                M10 == 0F && M11 == 1F && M12 == 0F && M13 == 0F && M14 == 0F &&
                M20 == 0F && M21 == 0F && M22 == 1F && M23 == 0F && M24 == 0F &&
                M30 == 0F && M31 == 0F && M32 == 0F && M33 == 1F && M34 == 0F;
        }
    }

    public float M00 { get; }

    public float M01 { get; }

    public float M02 { get; }

    public float M03 { get; }

    public float M04 { get; }

    public float M10 { get; }

    public float M11 { get; }

    public float M12 { get; }

    public float M13 { get; }

    public float M14 { get; }

    public float M20 { get; }

    public float M21 { get; }

    public float M22 { get; }

    public float M23 { get; }

    public float M24 { get; }

    public float M30 { get; }

    public float M31 { get; }

    public float M32 { get; }

    public float M33 { get; }

    public float M34 { get; }

    public float[] ToArray()
    {
        return new float[]
        {
            M00, M01, M02, M03, M04,
            M10, M11, M12, M13, M14,
            M20, M21, M22, M23, M24,
            M30, M31, M32, M33, M34,
        };
    }

    public static bool operator ==(ColorMatrix value1, ColorMatrix value2) => value1.Equals(value2);

    public static bool operator !=(ColorMatrix value1, ColorMatrix value2) => !value1.Equals(value2);

    public override bool Equals(object? obj)
    {
        return obj is ColorMatrix matrix && Equals(matrix);
    }

    public bool Equals(ColorMatrix other) =>
        M00 == other.M00
        && M01 == other.M01
        && M02 == other.M02
        && M03 == other.M03
        && M04 == other.M04
        && M10 == other.M10
        && M11 == other.M11
        && M12 == other.M12
        && M13 == other.M13
        && M14 == other.M14
        && M20 == other.M20
        && M21 == other.M21
        && M22 == other.M22
        && M23 == other.M23
        && M24 == other.M24
        && M30 == other.M30
        && M31 == other.M31
        && M32 == other.M32
        && M33 == other.M33
        && M34 == other.M34;

    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(M00);
        hash.Add(M01);
        hash.Add(M02);
        hash.Add(M03);
        hash.Add(M04);
        hash.Add(M10);
        hash.Add(M11);
        hash.Add(M12);
        hash.Add(M13);
        hash.Add(M14);
        hash.Add(M20);
        hash.Add(M21);
        hash.Add(M22);
        hash.Add(M23);
        hash.Add(M24);
        hash.Add(M30);
        hash.Add(M31);
        hash.Add(M32);
        hash.Add(M33);
        hash.Add(M34);
        return hash.ToHashCode();
    }

    public override string ToString()
    {
        CultureInfo ci = CultureInfo.CurrentCulture;

        return string.Format(ci, "{{ {{M00:{0} M01:{1} M02:{2} M03:{3} M04:{4}}} {{M10:{5} M11:{6} M12:{7} M13:{8} M14:{9}}} {{M20:{10} M21:{11} M22:{12} M23:{13} M24:{14}}} {{M30:{15} M31:{16} M32:{17} M33:{18} M34:{19}}} }}",
                             M00.ToString(ci), M01.ToString(ci), M02.ToString(ci), M03.ToString(ci), M04.ToString(ci),
                             M10.ToString(ci), M11.ToString(ci), M12.ToString(ci), M13.ToString(ci), M14.ToString(ci),
                             M20.ToString(ci), M21.ToString(ci), M22.ToString(ci), M23.ToString(ci), M24.ToString(ci),
                             M30.ToString(ci), M31.ToString(ci), M32.ToString(ci), M33.ToString(ci), M34.ToString(ci));
    }
}
