using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Text.Json.Serialization;

using Beutl.Converters;
using Beutl.Graphics.Transformation;
using Beutl.Utilities;

namespace Beutl.Graphics;

/// <summary>
/// A 3x3 matrix.
/// </summary>
/// <remakrs>Matrix layout:
///         | 1st col | 2nd col | 3r col |
/// 1st row | scaleX  | skewY   | persX  |
/// 2nd row | skewX   | scaleY  | persY  |
/// 3rd row | transX  | transY  | persZ  |
/// 
/// Note: Skia.SkMatrix uses a transposed layout (where for example skewX/skewY and perspp0/tranX are swapped).
/// </remakrs>
/// <remarks>
/// Initializes a new instance of the <see cref="Matrix"/> struct.
/// </remarks>
/// <param name="scaleX">The first element of the first row.</param>
/// <param name="skewY">The second element of the first row.</param>
/// <param name="persX">The third element of the first row.</param>
/// <param name="skewX">The first element of the second row.</param>
/// <param name="scaleY">The second element of the second row.</param>
/// <param name="persY">The third element of the second row.</param>
/// <param name="offsetX">The first element of the third row.</param>
/// <param name="offsetY">The second element of the third row.</param>
/// <param name="persZ">The third element of the third row.</param>
[JsonConverter(typeof(MatrixJsonConverter))]
[TypeConverter(typeof(MatrixConverter))]
public readonly struct Matrix(
    float scaleX, float skewY, float persX,
    float skewX, float scaleY, float persY,
    float offsetX, float offsetY, float persZ)
    : IEquatable<Matrix>,
      IParsable<Matrix>,
      ISpanParsable<Matrix>,
      IMultiplyOperators<Matrix, Matrix, Matrix>,
      IUnaryNegationOperators<Matrix, Matrix>,
      ITupleConvertible<Matrix, float>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Matrix"/> struct (equivalent to a 2x3 Matrix without perspective).
    /// </summary>
    /// <param name="scaleX">The first element of the first row.</param>
    /// <param name="skewY">The second element of the first row.</param>
    /// <param name="skewX">The first element of the second row.</param>
    /// <param name="scaleY">The second element of the second row.</param>
    /// <param name="offsetX">The first element of the third row.</param>
    /// <param name="offsetY">The second element of the third row.</param>
    public Matrix(
        float scaleX, float skewY,
        float skewX, float scaleY,
        float offsetX, float offsetY)
        : this(
              scaleX, skewY, 0,
              skewX, scaleY, 0,
              offsetX, offsetY, 1)
    {
    }

    /// <summary>
    /// Returns the multiplicative identity matrix.
    /// </summary>
    public static Matrix Identity { get; } = new Matrix(
        1.0f, 0.0f, 0.0f,
        0.0f, 1.0f, 0.0f,
        0.0f, 0.0f, 1.0f);

    /// <summary>
    /// Returns whether the matrix is the identity matrix.
    /// </summary>
    public bool IsIdentity => Equals(Identity);

    /// <summary>
    /// HasInverse Property - returns true if this matrix is invertible, false otherwise.
    /// </summary>
    public bool HasInverse => !MathUtilities.IsZero(GetDeterminant());

    /// <summary>
    /// The first element of the first row (scaleX).
    /// </summary>
    public float M11 { get; } = scaleX;

    /// <summary>
    /// The second element of the first row (skewY).
    /// </summary>
    public float M12 { get; } = skewY;

    /// <summary>
    /// The third element of the first row (persX: input x-axis perspective factor).
    /// </summary>
    public float M13 { get; } = persX;

    /// <summary>
    /// The first element of the second row (skewX).
    /// </summary>
    public float M21 { get; } = skewX;

    /// <summary>
    /// The second element of the second row (scaleY).
    /// </summary>
    public float M22 { get; } = scaleY;

    /// <summary>
    /// The third element of the second row (persY: input y-axis perspective factor).
    /// </summary>
    public float M23 { get; } = persY;

    /// <summary>
    /// The first element of the third row (offsetX/translateX).
    /// </summary>
    public float M31 { get; } = offsetX;

    /// <summary>
    /// The second element of the third row (offsetY/translateY).
    /// </summary>
    public float M32 { get; } = offsetY;

    /// <summary>
    /// The third element of the third row (persZ: perspective scale factor).
    /// </summary>
    public float M33 { get; } = persZ;

    static int ITupleConvertible<Matrix, float>.TupleLength => 9;

    /// <summary>
    /// Multiplies two matrices together and returns the resulting matrix.
    /// </summary>
    /// <param name="value1">The first source matrix.</param>
    /// <param name="value2">The second source matrix.</param>
    /// <returns>The product matrix.</returns>
    public static Matrix operator *(Matrix value1, Matrix value2)
    {
        return new Matrix(
            (value1.M11 * value2.M11) + (value1.M12 * value2.M21) + (value1.M13 * value2.M31),
            (value1.M11 * value2.M12) + (value1.M12 * value2.M22) + (value1.M13 * value2.M32),
            (value1.M11 * value2.M13) + (value1.M12 * value2.M23) + (value1.M13 * value2.M33),
            (value1.M21 * value2.M11) + (value1.M22 * value2.M21) + (value1.M23 * value2.M31),
            (value1.M21 * value2.M12) + (value1.M22 * value2.M22) + (value1.M23 * value2.M32),
            (value1.M21 * value2.M13) + (value1.M22 * value2.M23) + (value1.M23 * value2.M33),
            (value1.M31 * value2.M11) + (value1.M32 * value2.M21) + (value1.M33 * value2.M31),
            (value1.M31 * value2.M12) + (value1.M32 * value2.M22) + (value1.M33 * value2.M32),
            (value1.M31 * value2.M13) + (value1.M32 * value2.M23) + (value1.M33 * value2.M33));
    }

    /// <summary>
    /// Negates the given matrix by multiplying all values by -1.
    /// </summary>
    /// <param name="value">The source matrix.</param>
    /// <returns>The negated matrix.</returns>
    public static Matrix operator -(Matrix value)
    {
        return value.Invert();
    }

    /// <summary>
    /// Returns a boolean indicating whether the given matrices are equal.
    /// </summary>
    /// <param name="value1">The first source matrix.</param>
    /// <param name="value2">The second source matrix.</param>
    /// <returns>True if the matrices are equal; False otherwise.</returns>
    public static bool operator ==(Matrix value1, Matrix value2)
    {
        return value1.Equals(value2);
    }

    /// <summary>
    /// Returns a boolean indicating whether the given matrices are not equal.
    /// </summary>
    /// <param name="value1">The first source matrix.</param>
    /// <param name="value2">The second source matrix.</param>
    /// <returns>True if the matrices are not equal; False if they are equal.</returns>
    public static bool operator !=(Matrix value1, Matrix value2)
    {
        return !value1.Equals(value2);
    }

    /// <summary>
    /// Creates a rotation matrix using the given rotation in radians.
    /// </summary>
    /// <param name="radians">The amount of rotation, in radians.</param>
    /// <returns>A rotation matrix.</returns>
    public static Matrix CreateRotation(float radians)
    {
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        return new Matrix(cos, sin, -sin, cos, 0, 0);
    }

    /// <summary>
    /// Creates a skew matrix from the given axis skew angles in radians.
    /// </summary>
    /// <param name="xAngle">The amount of skew along the X-axis, in radians.</param>
    /// <param name="yAngle">The amount of skew along the Y-axis, in radians.</param>
    /// <returns>A rotation matrix.</returns>
    public static Matrix CreateSkew(float xAngle, float yAngle)
    {
        float tanX = MathF.Tan(xAngle);
        float tanY = MathF.Tan(yAngle);
        return new Matrix(1.0f, tanY, tanX, 1.0f, 0.0f, 0.0f);
    }

    /// <summary>
    /// Creates a scale matrix from the given X and Y components.
    /// </summary>
    /// <param name="xScale">Value to scale by on the X-axis.</param>
    /// <param name="yScale">Value to scale by on the Y-axis.</param>
    /// <returns>A scaling matrix.</returns>
    public static Matrix CreateScale(float xScale, float yScale)
    {
        return new Matrix(xScale, 0, 0, yScale, 0, 0);
    }

    /// <summary>
    /// Creates a scale matrix from the given vector scale.
    /// </summary>
    /// <param name="scales">The scale to use.</param>
    /// <returns>A scaling matrix.</returns>
    public static Matrix CreateScale(Vector scales)
    {
        return CreateScale(scales.X, scales.Y);
    }

    /// <summary>
    /// Creates a translation matrix from the given vector.
    /// </summary>
    /// <param name="position">The translation position.</param>
    /// <returns>A translation matrix.</returns>
    public static Matrix CreateTranslation(Vector position)
    {
        return CreateTranslation(position.X, position.Y);
    }

    /// <summary>
    /// Creates a translation matrix from the given X and Y components.
    /// </summary>
    /// <param name="xPosition">The X position.</param>
    /// <param name="yPosition">The Y position.</param>
    /// <returns>A translation matrix.</returns>
    public static Matrix CreateTranslation(float xPosition, float yPosition)
    {
        return new Matrix(1.0f, 0.0f, 0.0f, 1.0f, xPosition, yPosition);
    }

    /// <summary>
    /// Appends another matrix as post-multiplication operation.
    /// Equivalent to this * value;
    /// </summary>
    /// <param name="value">A matrix.</param>
    /// <returns>Post-multiplied matrix.</returns>
    public Matrix Append(Matrix value)
    {
        return this * value;
    }

    /// <summary>
    /// Prepends another matrix as pre-multiplication operation.
    /// Equivalent to value * this;
    /// </summary>
    /// <param name="value">A matrix.</param>
    /// <returns>Pre-multiplied matrix.</returns>
    public Matrix Prepend(Matrix value)
    {
        return value * this;
    }

    /// <summary>
    /// Calculates the determinant for this matrix.
    /// </summary>
    /// <returns>The determinant.</returns>
    /// <remarks>
    /// The determinant is calculated by expanding the matrix with a third column whose
    /// values are (0,0,1).
    /// </remarks>
    public float GetDeterminant()
    {
        // implemented using "Laplace expansion":
        return M11 * (M22 * M33 - M23 * M32)
             - M12 * (M21 * M33 - M23 * M31)
             + M13 * (M21 * M32 - M22 * M31);
    }

    /// <summary>
    ///  Transforms the point with the matrix
    /// </summary>
    /// <param name="p">The point to be transformed</param>
    /// <returns>The transformed point</returns>
    public Point Transform(Point p)
    {
        Point transformedResult;

        // If this matrix contains a non-affine transform with need to extend
        // the point to a 3D vector and flatten it back for 2d display
        // by multiplying X and Y with the inverse of the Z axis.
        // The code below also works with affine transformations, but for performance (and compatibility)
        // reasons we will use the more complex calculation only if necessary
        if (ContainsPerspective())
        {
            var m44 = new Matrix4x4(
                M11, M12, M13, 0,
                M21, M22, M23, 0,
                M31, M32, M33, 0,
                0, 0, 0, 1
            );

            var vector = new Vector3(p.X, p.Y, 1);
            var transformedVector = Vector3.Transform(vector, m44);
            float z = 1 / transformedVector.Z;

            transformedResult = new Point(transformedVector.X * z, transformedVector.Y * z);
        }
        else
        {
            return new Point(
                (p.X * M11) + (p.Y * M21) + M31,
                (p.X * M12) + (p.Y * M22) + M32);
        }

        return transformedResult;
    }

    /// <summary>
    /// Returns a boolean indicating whether the matrix is equal to the other given matrix.
    /// </summary>
    /// <param name="other">The other matrix to test equality against.</param>
    /// <returns>True if this matrix is equal to other; False otherwise.</returns>
    public bool Equals(Matrix other)
    {
        return M11 == other.M11 &&
               M12 == other.M12 &&
               M13 == other.M13 &&
               M21 == other.M21 &&
               M22 == other.M22 &&
               M23 == other.M23 &&
               M31 == other.M31 &&
               M32 == other.M32 &&
               M33 == other.M33;
    }

    /// <summary>
    /// Returns a boolean indicating whether the given Object is equal to this matrix instance.
    /// </summary>
    /// <param name="obj">The Object to compare against.</param>
    /// <returns>True if the Object is equal to this matrix; False otherwise.</returns>
    public override bool Equals(object? obj)
    {
        return obj is Matrix other && Equals(other);
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(M11);
        hash.Add(M12);
        hash.Add(M13);
        hash.Add(M21);
        hash.Add(M22);
        hash.Add(M23);
        hash.Add(M31);
        hash.Add(M32);
        hash.Add(M33);
        return hash.ToHashCode();
    }

    /// <summary>
    ///  Determines if the current matrix contains perspective (non-affine) transforms (true) or only (affine) transforms that could be mapped into an 2x3 matrix (false).
    /// </summary>
    private bool ContainsPerspective()
    {
        return M13 != 0 || M23 != 0 || M33 != 1;
    }

    /// <summary>
    /// Returns a String representing this matrix instance.
    /// </summary>
    /// <returns>The string representation.</returns>
    public override string ToString()
    {
        if (ContainsPerspective())
        {
            return FormattableString.Invariant($"matrix({M11}, {M12}, {M13}, {M21}, {M22}, {M23}, {M31}, {M32}, {M33})");
        }
        else
        {
            return FormattableString.Invariant($"matrix({M11}, {M12}, {M21}, {M22}, {M31}, {M32})");
        }
    }

    /// <summary>
    /// Attempts to invert the Matrix.
    /// </summary>
    /// <returns>The inverted matrix or <see langword="null"/> when matrix is not invertible.</returns>
    public bool TryInvert(out Matrix inverted)
    {
        float d = GetDeterminant();

        if (MathUtilities.IsZero(d))
        {
            inverted = default;

            return false;
        }

        float invdet = 1 / d;

        inverted = new Matrix(
            (M22 * M33 - M32 * M23) * invdet,
            (M13 * M32 - M12 * M33) * invdet,
            (M12 * M23 - M13 * M22) * invdet,
            (M23 * M31 - M21 * M33) * invdet,
            (M11 * M33 - M13 * M31) * invdet,
            (M21 * M13 - M11 * M23) * invdet,
            (M21 * M32 - M31 * M22) * invdet,
            (M31 * M12 - M11 * M32) * invdet,
            (M11 * M22 - M21 * M12) * invdet);

        return true;
    }

    /// <summary>
    /// Inverts the Matrix.
    /// </summary>
    /// <exception cref="InvalidOperationException">Matrix is not invertible.</exception>
    /// <returns>The inverted matrix.</returns>
    public Matrix Invert()
    {
        if (!TryInvert(out Matrix inverted))
        {
            throw new InvalidOperationException("Transform is not invertible.");
        }

        return inverted;
    }

    /// <summary>
    /// Parses a <see cref="Matrix"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="result">The <see cref="Matrix"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(string s, out Matrix result)
    {
        return TryParse(s.AsSpan(), out result);
    }

    /// <summary>
    /// Parses a <see cref="Matrix"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="result">The <see cref="Matrix"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, out Matrix result)
    {
        try
        {
            result = Parse(s);
            return true;
        }
        catch
        {
            result = default;
            return false;
        }
    }

    /// <summary>
    /// Parses a <see cref="Matrix"/> string.
    /// </summary>
    /// <param name="s">Six or nine comma-delimited float values (m11, m12, m21, m22, offsetX, offsetY[, persX, persY, persZ]) that describe the new <see cref="Matrix"/></param>
    /// <returns>The <see cref="Matrix"/>.</returns>
    public static Matrix Parse(string s)
    {
        return Parse(s.AsSpan());
    }

    /// <summary>
    /// Parses a <see cref="Matrix"/> string.
    /// </summary>
    /// <param name="s">Six or nine comma-delimited float values (m11, m12, m21, m22, offsetX, offsetY[, persX, persY, persZ]) that describe the new <see cref="Matrix"/></param>
    /// <returns>The <see cref="Matrix"/>.</returns>
    public static Matrix Parse(ReadOnlySpan<char> s)
    {
        return TransformParser.ParseMatrix(s);
    }

    static Matrix IParsable<Matrix>.Parse(string s, IFormatProvider? provider)
    {
        return Parse(s);
    }

    static bool IParsable<Matrix>.TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out Matrix result)
    {
        result = default;
        return s != null && TryParse(s, out result);
    }

    static Matrix ISpanParsable<Matrix>.Parse(ReadOnlySpan<char> s, IFormatProvider? provider)
    {
        return Parse(s);
    }

    static bool ISpanParsable<Matrix>.TryParse([NotNullWhen(true)] ReadOnlySpan<char> s, IFormatProvider? provider, [MaybeNullWhen(false)] out Matrix result)
    {
        return TryParse(s, out result);
    }

    /// <summary>
    /// Decomposes given matrix into transform operations.
    /// </summary>
    /// <param name="matrix">Matrix to decompose.</param>
    /// <param name="decomposed">Decomposed matrix.</param>
    /// <returns>The status of the operation.</returns>        
    public bool TryDecomposeTransform(out Vector translate, out Vector scale, out Vector skew, out float angle)
    {
        float determinant = GetDeterminant();

        if (MathUtilities.IsZero(determinant) || ContainsPerspective())
        {
            translate = default;
            scale = default;
            skew = default;
            angle = 0;
            return false;
        }

        float m11 = M11;
        float m21 = M21;
        float m12 = M12;
        float m22 = M22;

        // Translation.
        translate = new Vector(M31, M32);

        // Scale sign.
        float scaleX = 1f;
        float scaleY = 1f;

        if (determinant < 0)
        {
            if (m11 < m22)
            {
                scaleX *= -1f;
            }
            else
            {
                scaleY *= -1f;
            }
        }

        // X Scale.
        scaleX *= MathF.Sqrt(m11 * m11 + m12 * m12);

        m11 /= scaleX;
        m12 /= scaleX;

        // XY Shear.
        float scaledShear = m11 * m21 + m12 * m22;

        m21 -= m11 * scaledShear;
        m22 -= m12 * scaledShear;

        // Y Scale.
        scaleY *= MathF.Sqrt(m21 * m21 + m22 * m22);

        scale = new Vector(scaleX, scaleY);
        skew = new Vector(scaledShear / scaleY, 0f);
        angle = MathF.Atan2(m12, m11);

        return true;
    }

    public static Matrix ComposeTransform(Vector translate, Vector scale, Vector skew, float angle)
    {
        return Identity
            .Prepend(CreateTranslation(translate))
            .Prepend(CreateRotation(angle))
            .Prepend(CreateSkew(skew.X, skew.Y))
            .Prepend(CreateScale(scale));
    }

    static void ITupleConvertible<Matrix, float>.ConvertTo(Matrix self, Span<float> tuple)
    {
        tuple[0] = self.M11;
        tuple[1] = self.M12;
        tuple[2] = self.M13;
        tuple[3] = self.M21;
        tuple[4] = self.M22;
        tuple[5] = self.M23;
        tuple[6] = self.M31;
        tuple[7] = self.M32;
        tuple[8] = self.M33;
    }

    static void ITupleConvertible<Matrix, float>.ConvertFrom(Span<float> tuple, out Matrix self)
    {
        self = new Matrix
        (
            tuple[0], tuple[1], tuple[2],
            tuple[3], tuple[4], tuple[5],
            tuple[6], tuple[7], tuple[8]
        );
    }
}
