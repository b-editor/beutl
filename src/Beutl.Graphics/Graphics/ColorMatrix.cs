using System.Globalization;
using System.Runtime.InteropServices;

namespace BeUtl.Graphics;

[StructLayout(LayoutKind.Sequential)]
public unsafe struct ColorMatrix : IEquatable<ColorMatrix>
{
    private fixed float _array[25];

    public ColorMatrix(
        float m00, float m01, float m02, float m03, float m04,
        float m10, float m11, float m12, float m13, float m14,
        float m20, float m21, float m22, float m23, float m24,
        float m30, float m31, float m32, float m33, float m34,
        float m40, float m41, float m42, float m43, float m44)
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

        M40 = m40;
        M41 = m41;
        M42 = m42;
        M43 = m43;
        M44 = m44;
        M44 = m44;
    }

    public static ColorMatrix Identity { get; } =
        new ColorMatrix(
            1F, 0F, 0F, 0F, 0F,
            0F, 1F, 0F, 0F, 0F,
            0F, 0F, 1F, 0F, 0F,
            0F, 0F, 0F, 1F, 0F,
            0F, 0F, 0F, 0F, 1F);

    public bool IsIdentity
    {
        get
        {
            // Check diagonal element first for early out.
            return
                M00 == 1F && M01 == 0F && M02 == 0F && M03 == 0F && M04 == 0F &&
                M10 == 0F && M11 == 1F && M12 == 0F && M13 == 0F && M14 == 0F &&
                M20 == 0F && M21 == 0F && M22 == 1F && M23 == 0F && M24 == 0F &&
                M30 == 0F && M31 == 0F && M32 == 0F && M33 == 1F && M34 == 0F &&
                M40 == 0F && M41 == 0F && M42 == 0F && M43 == 0F && M44 == 1F;
        }
    }

    public ref float M00 => ref _array[0];

    public ref float M01 => ref _array[1];

    public ref float M02 => ref _array[2];

    public ref float M03 => ref _array[3];

    public ref float M04 => ref _array[4];

    public ref float M10 => ref _array[5];

    public ref float M11 => ref _array[6];

    public ref float M12 => ref _array[7];

    public ref float M13 => ref _array[8];

    public ref float M14 => ref _array[9];

    public ref float M20 => ref _array[10];

    public ref float M21 => ref _array[11];

    public ref float M22 => ref _array[12];

    public ref float M23 => ref _array[13];

    public ref float M24 => ref _array[14];

    public ref float M30 => ref _array[15];

    public ref float M31 => ref _array[16];

    public ref float M32 => ref _array[17];

    public ref float M33 => ref _array[18];

    public ref float M34 => ref _array[19];

    public ref float M40 => ref _array[20];

    public ref float M41 => ref _array[21];

    public ref float M42 => ref _array[22];

    public ref float M43 => ref _array[23];

    public ref float M44 => ref _array[24];

    public ref float this[int row, int column]
    {
        get
        {
            if (row is < 0 or >= 5) throw new ArgumentOutOfRangeException(nameof(row));
            if (column is < 0 or >= 5) throw new ArgumentOutOfRangeException(nameof(column));

            int pos = row * 5;
            return ref _array[pos + column];
        }
    }

    public static bool operator ==(ColorMatrix value1, ColorMatrix value2) => value1.Equals(value2);

    public static bool operator !=(ColorMatrix value1, ColorMatrix value2) => !value1.Equals(value2);

    public static ColorMatrix operator +(ColorMatrix value1, ColorMatrix value2)
    {
        var m = default(ColorMatrix);

        m.M00 = value1.M00 + value2.M00;
        m.M01 = value1.M01 + value2.M01;
        m.M02 = value1.M02 + value2.M02;
        m.M03 = value1.M03 + value2.M03;
        m.M04 = value1.M04 + value2.M04;

        m.M10 = value1.M10 + value2.M10;
        m.M11 = value1.M11 + value2.M11;
        m.M12 = value1.M12 + value2.M12;
        m.M13 = value1.M13 + value2.M13;
        m.M14 = value1.M14 + value2.M14;

        m.M20 = value1.M20 + value2.M20;
        m.M21 = value1.M21 + value2.M21;
        m.M22 = value1.M22 + value2.M22;
        m.M23 = value1.M23 + value2.M23;
        m.M24 = value1.M24 + value2.M24;

        m.M30 = value1.M30 + value2.M30;
        m.M31 = value1.M31 + value2.M31;
        m.M32 = value1.M32 + value2.M32;
        m.M33 = value1.M33 + value2.M33;
        m.M34 = value1.M34 + value2.M34;

        m.M40 = value1.M40 + value2.M40;
        m.M41 = value1.M41 + value2.M41;
        m.M42 = value1.M42 + value2.M42;
        m.M43 = value1.M43 + value2.M43;
        m.M44 = value1.M44 + value2.M43;

        return m;
    }

    public static ColorMatrix operator -(ColorMatrix value1, ColorMatrix value2)
    {
        var m = default(ColorMatrix);

        m.M00 = value1.M00 - value2.M00;
        m.M01 = value1.M01 - value2.M01;
        m.M02 = value1.M02 - value2.M02;
        m.M03 = value1.M03 - value2.M03;
        m.M04 = value1.M04 - value2.M04;

        m.M10 = value1.M10 - value2.M10;
        m.M11 = value1.M11 - value2.M11;
        m.M12 = value1.M12 - value2.M12;
        m.M13 = value1.M13 - value2.M13;
        m.M14 = value1.M14 - value2.M14;

        m.M20 = value1.M20 - value2.M20;
        m.M21 = value1.M21 - value2.M21;
        m.M22 = value1.M22 - value2.M22;
        m.M23 = value1.M23 - value2.M23;
        m.M24 = value1.M24 - value2.M24;

        m.M30 = value1.M30 - value2.M30;
        m.M31 = value1.M31 - value2.M31;
        m.M32 = value1.M32 - value2.M32;
        m.M33 = value1.M33 - value2.M33;
        m.M34 = value1.M34 - value2.M34;

        m.M40 = value1.M40 - value2.M40;
        m.M41 = value1.M41 - value2.M41;
        m.M42 = value1.M42 - value2.M42;
        m.M43 = value1.M43 - value2.M43;
        m.M44 = value1.M44 - value2.M43;

        return m;
    }

    public static ColorMatrix operator -(ColorMatrix value)
    {
        var m = default(ColorMatrix);

        m.M00 = -value.M00;
        m.M01 = -value.M01;
        m.M02 = -value.M02;
        m.M03 = -value.M03;
        m.M04 = -value.M04;

        m.M10 = -value.M10;
        m.M11 = -value.M11;
        m.M12 = -value.M12;
        m.M13 = -value.M13;
        m.M14 = -value.M14;

        m.M20 = -value.M20;
        m.M21 = -value.M21;
        m.M22 = -value.M22;
        m.M23 = -value.M23;
        m.M24 = -value.M24;

        m.M30 = -value.M30;
        m.M31 = -value.M31;
        m.M32 = -value.M32;
        m.M33 = -value.M33;
        m.M34 = -value.M34;

        m.M40 = -value.M40;
        m.M41 = -value.M41;
        m.M42 = -value.M42;
        m.M43 = -value.M43;
        m.M44 = -value.M44;

        return m;
    }

    public static ColorMatrix operator *(ColorMatrix value1, ColorMatrix value2)
    {
        var m = default(ColorMatrix);

        // First row
        m.M00 = (value1.M00 * value2.M00) + (value1.M01 * value2.M10) + (value1.M02 * value2.M20) + (value1.M03 * value2.M30) + (value1.M04 * value2.M40);
        m.M01 = (value1.M00 * value2.M01) + (value1.M01 * value2.M11) + (value1.M02 * value2.M21) + (value1.M03 * value2.M31) + (value1.M04 * value2.M41);
        m.M02 = (value1.M00 * value2.M02) + (value1.M01 * value2.M12) + (value1.M02 * value2.M22) + (value1.M03 * value2.M32) + (value1.M04 * value2.M42);
        m.M03 = (value1.M00 * value2.M03) + (value1.M01 * value2.M13) + (value1.M02 * value2.M23) + (value1.M03 * value2.M33) + (value1.M04 * value2.M43);
        m.M04 = (value1.M00 * value2.M04) + (value1.M01 * value2.M14) + (value1.M02 * value2.M24) + (value1.M03 * value2.M34) + (value1.M04 * value2.M44);

        // Second row
        m.M10 = (value1.M10 * value2.M00) + (value1.M11 * value2.M10) + (value1.M12 * value2.M20) + (value1.M13 * value2.M30) + (value1.M14 * value2.M40);
        m.M11 = (value1.M10 * value2.M01) + (value1.M11 * value2.M11) + (value1.M12 * value2.M21) + (value1.M13 * value2.M31) + (value1.M14 * value2.M41);
        m.M12 = (value1.M10 * value2.M02) + (value1.M11 * value2.M12) + (value1.M12 * value2.M22) + (value1.M13 * value2.M32) + (value1.M14 * value2.M42);
        m.M13 = (value1.M10 * value2.M03) + (value1.M11 * value2.M13) + (value1.M12 * value2.M23) + (value1.M13 * value2.M33) + (value1.M14 * value2.M43);
        m.M14 = (value1.M10 * value2.M04) + (value1.M11 * value2.M14) + (value1.M12 * value2.M24) + (value1.M13 * value2.M34) + (value1.M14 * value2.M44);

        // Third row
        m.M20 = (value1.M20 * value2.M00) + (value1.M21 * value2.M10) + (value1.M22 * value2.M20) + (value1.M23 * value2.M30) + (value1.M24 * value2.M40);
        m.M21 = (value1.M20 * value2.M01) + (value1.M21 * value2.M11) + (value1.M22 * value2.M21) + (value1.M23 * value2.M31) + (value1.M24 * value2.M41);
        m.M22 = (value1.M20 * value2.M02) + (value1.M21 * value2.M12) + (value1.M22 * value2.M22) + (value1.M23 * value2.M32) + (value1.M24 * value2.M42);
        m.M23 = (value1.M20 * value2.M03) + (value1.M21 * value2.M13) + (value1.M22 * value2.M23) + (value1.M23 * value2.M33) + (value1.M24 * value2.M43);
        m.M24 = (value1.M20 * value2.M04) + (value1.M21 * value2.M14) + (value1.M22 * value2.M24) + (value1.M23 * value2.M34) + (value1.M24 * value2.M44);

        // Fourth row
        m.M30 = (value1.M30 * value2.M00) + (value1.M31 * value2.M10) + (value1.M32 * value2.M20) + (value1.M33 * value2.M30) + (value1.M34 * value2.M40);
        m.M31 = (value1.M30 * value2.M01) + (value1.M31 * value2.M11) + (value1.M32 * value2.M21) + (value1.M33 * value2.M31) + (value1.M34 * value2.M41);
        m.M32 = (value1.M30 * value2.M02) + (value1.M31 * value2.M12) + (value1.M32 * value2.M22) + (value1.M33 * value2.M32) + (value1.M34 * value2.M42);
        m.M33 = (value1.M30 * value2.M03) + (value1.M31 * value2.M13) + (value1.M32 * value2.M23) + (value1.M33 * value2.M33) + (value1.M34 * value2.M43);
        m.M34 = (value1.M30 * value2.M04) + (value1.M31 * value2.M14) + (value1.M32 * value2.M24) + (value1.M33 * value2.M34) + (value1.M34 * value2.M44);

        // Fifth row
        m.M40 = (value1.M40 * value2.M00) + (value1.M41 * value2.M10) + (value1.M42 * value2.M20) + (value1.M43 * value2.M30) + (value1.M44 * value2.M40);
        m.M41 = (value1.M40 * value2.M01) + (value1.M41 * value2.M11) + (value1.M42 * value2.M21) + (value1.M43 * value2.M31) + (value1.M44 * value2.M41);
        m.M42 = (value1.M40 * value2.M02) + (value1.M41 * value2.M12) + (value1.M42 * value2.M22) + (value1.M43 * value2.M32) + (value1.M44 * value2.M42);
        m.M43 = (value1.M40 * value2.M03) + (value1.M41 * value2.M13) + (value1.M42 * value2.M23) + (value1.M43 * value2.M33) + (value1.M44 * value2.M43);
        m.M44 = (value1.M40 * value2.M04) + (value1.M41 * value2.M14) + (value1.M42 * value2.M24) + (value1.M43 * value2.M34) + (value1.M44 * value2.M44);

        return m;
    }

    public static ColorVector operator *(ColorVector value1, ColorMatrix value2)
    {
        var m = default(ColorVector);

        m.R = (value1.R * value2.M00) + (value1.G * value2.M10) + (value1.B * value2.M20) + (value1.A * value2.M30) + (value1.W * value2.M40);
        m.G = (value1.R * value2.M01) + (value1.G * value2.M11) + (value1.B * value2.M21) + (value1.A * value2.M31) + (value1.W * value2.M41);
        m.B = (value1.R * value2.M02) + (value1.G * value2.M12) + (value1.B * value2.M22) + (value1.A * value2.M32) + (value1.W * value2.M42);
        m.A = (value1.R * value2.M03) + (value1.G * value2.M13) + (value1.B * value2.M23) + (value1.A * value2.M33) + (value1.W * value2.M43);
        m.W = (value1.R * value2.M04) + (value1.G * value2.M14) + (value1.B * value2.M24) + (value1.A * value2.M34) + (value1.W * value2.M44);

        return m;
    }

    public static ColorMatrix operator *(ColorMatrix value1, float value2)
    {
        var m = default(ColorMatrix);

        m.M00 = value1.M00 * value2;
        m.M01 = value1.M01 * value2;
        m.M02 = value1.M02 * value2;
        m.M03 = value1.M03 * value2;
        m.M04 = value1.M04 * value2;

        m.M10 = value1.M10 * value2;
        m.M11 = value1.M11 * value2;
        m.M12 = value1.M12 * value2;
        m.M13 = value1.M13 * value2;
        m.M14 = value1.M14 * value2;

        m.M20 = value1.M20 * value2;
        m.M21 = value1.M21 * value2;
        m.M22 = value1.M22 * value2;
        m.M23 = value1.M23 * value2;
        m.M24 = value1.M24 * value2;

        m.M30 = value1.M30 * value2;
        m.M31 = value1.M31 * value2;
        m.M32 = value1.M32 * value2;
        m.M33 = value1.M33 * value2;
        m.M34 = value1.M34 * value2;

        m.M40 = value1.M40 * value2;
        m.M41 = value1.M41 * value2;
        m.M42 = value1.M42 * value2;
        m.M43 = value1.M43 * value2;
        m.M44 = value1.M44 * value2;

        return m;
    }

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
        && M34 == other.M34
        && M40 == other.M40
        && M41 == other.M41
        && M42 == other.M42
        && M43 == other.M43
        && M44 == other.M44;

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
        hash.Add(M40);
        hash.Add(M41);
        hash.Add(M42);
        hash.Add(M43);
        hash.Add(M44);
        return hash.ToHashCode();
    }

    public override string ToString()
    {
        CultureInfo ci = CultureInfo.CurrentCulture;

        return string.Format(ci, "{{ {{M00:{0} M01:{1} M02:{2} M03:{3} M04:{4}}} {{M10:{5} M11:{6} M12:{7} M13:{8} M14:{9}}} {{M20:{10} M21:{11} M22:{12} M23:{13} M24:{14}}} {{M30:{15} M31:{16} M32:{17} M33:{18} M34:{19}}} {{M40:{20} M41:{21} M42:{22} M43:{23} M44:{24}}} }}",
                             M00.ToString(ci), M01.ToString(ci), M02.ToString(ci), M03.ToString(ci), M04.ToString(ci),
                             M10.ToString(ci), M11.ToString(ci), M12.ToString(ci), M13.ToString(ci), M14.ToString(ci),
                             M20.ToString(ci), M21.ToString(ci), M22.ToString(ci), M23.ToString(ci), M24.ToString(ci),
                             M30.ToString(ci), M31.ToString(ci), M32.ToString(ci), M33.ToString(ci), M34.ToString(ci),
                             M40.ToString(ci), M41.ToString(ci), M42.ToString(ci), M43.ToString(ci), M44.ToString(ci));
    }
}
