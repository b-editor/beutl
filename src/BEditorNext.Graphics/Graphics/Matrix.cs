using System.Globalization;
using System.Text.Json.Serialization;

using BEditorNext.Converters;
using BEditorNext.Utilities;

namespace BEditorNext.Graphics;

/// <summary>
/// A 2x3 matrix.
/// </summary>
[JsonConverter(typeof(MatrixJsonConverter))]
public readonly struct Matrix : IEquatable<Matrix>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Matrix"/> struct.
    /// </summary>
    /// <param name="m11">The first element of the first row.</param>
    /// <param name="m12">The second element of the first row.</param>
    /// <param name="m21">The first element of the second row.</param>
    /// <param name="m22">The second element of the second row.</param>
    /// <param name="offsetX">The first element of the third row.</param>
    /// <param name="offsetY">The second element of the third row.</param>
    public Matrix(
        float m11,
        float m12,
        float m21,
        float m22,
        float offsetX,
        float offsetY)
    {
        M11 = m11;
        M12 = m12;
        M21 = m21;
        M22 = m22;
        M31 = offsetX;
        M32 = offsetY;
    }

    /// <summary>
    /// Returns the multiplicative identity matrix.
    /// </summary>
    public static Matrix Identity { get; } = new Matrix(1.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f);

    /// <summary>
    /// Returns whether the matrix is the identity matrix.
    /// </summary>
    public bool IsIdentity => Equals(Identity);

    /// <summary>
    /// HasInverse Property - returns true if this matrix is invertible, false otherwise.
    /// </summary>
    public bool HasInverse => !MathUtilities.IsZero(GetDeterminant());

    /// <summary>
    /// The first element of the first row
    /// </summary>
    public float M11 { get; }

    /// <summary>
    /// The second element of the first row
    /// </summary>
    public float M12 { get; }

    /// <summary>
    /// The first element of the second row
    /// </summary>
    public float M21 { get; }

    /// <summary>
    /// The second element of the second row
    /// </summary>
    public float M22 { get; }

    /// <summary>
    /// The first element of the third row
    /// </summary>
    public float M31 { get; }

    /// <summary>
    /// The second element of the third row
    /// </summary>
    public float M32 { get; }

    /// <summary>
    /// Multiplies two matrices together and returns the resulting matrix.
    /// </summary>
    /// <param name="value1">The first source matrix.</param>
    /// <param name="value2">The second source matrix.</param>
    /// <returns>The product matrix.</returns>
    public static Matrix operator *(Matrix value1, Matrix value2)
    {
        return new Matrix(
            (value1.M11 * value2.M11) + (value1.M12 * value2.M21),
            (value1.M11 * value2.M12) + (value1.M12 * value2.M22),
            (value1.M21 * value2.M11) + (value1.M22 * value2.M21),
            (value1.M21 * value2.M12) + (value1.M22 * value2.M22),
            (value1.M31 * value2.M11) + (value1.M32 * value2.M21) + value2.M31,
            (value1.M31 * value2.M12) + (value1.M32 * value2.M22) + value2.M32);
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
        return CreateScale(new Vector(xScale, yScale));
    }

    /// <summary>
    /// Creates a scale matrix from the given vector scale.
    /// </summary>
    /// <param name="scales">The scale to use.</param>
    /// <returns>A scaling matrix.</returns>
    public static Matrix CreateScale(Vector scales)
    {
        return new Matrix(scales.X, 0, 0, scales.Y, 0, 0);
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
    /// Converts an angle in degrees to radians.
    /// </summary>
    /// <param name="angle">The angle in degrees.</param>
    /// <returns>The angle in radians.</returns>
    public static float ToRadians(float angle)
    {
        return angle * 0.0174532925f;
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
    /// Prpends another matrix as pre-multiplication operation.
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
        return (M11 * M22) - (M12 * M21);
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
               M21 == other.M21 &&
               M22 == other.M22 &&
               M31 == other.M31 &&
               M32 == other.M32;
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
        return M11.GetHashCode() + M12.GetHashCode() +
               M21.GetHashCode() + M22.GetHashCode() +
               M31.GetHashCode() + M32.GetHashCode();
    }

    /// <summary>
    /// Returns a String representing this matrix instance.
    /// </summary>
    /// <returns>The string representation.</returns>
    public override string ToString()
    {
        return FormattableString.Invariant($"{M11}, {M12}, {M21}, {M22}, {M31}, {M32}");
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

        inverted = new Matrix(
            M22 / d,
            -M12 / d,
            -M21 / d,
            M11 / d,
            ((M21 * M32) - (M22 * M31)) / d,
            ((M12 * M31) - (M11 * M32)) / d);

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
    /// <param name="s">Six comma-delimited float values (m11, m12, m21, m22, offsetX, offsetY) that describe the new <see cref="Matrix"/></param>
    /// <returns>The <see cref="Matrix"/>.</returns>
    public static Matrix Parse(string s)
    {
        using (var tokenizer = new StringTokenizer(s, CultureInfo.InvariantCulture, exceptionMessage: "Invalid Matrix."))
        {
            return new Matrix(
                tokenizer.ReadSingle(),
                tokenizer.ReadSingle(),
                tokenizer.ReadSingle(),
                tokenizer.ReadSingle(),
                tokenizer.ReadSingle(),
                tokenizer.ReadSingle()
            );
        }
    }

    /// <summary>
    /// Decomposes given matrix into transform operations.
    /// </summary>
    /// <param name="matrix">Matrix to decompose.</param>
    /// <param name="decomposed">Decomposed matrix.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryDecomposeTransform(Matrix matrix, out Decomposed decomposed)
    {
        decomposed = default;

        float determinant = matrix.GetDeterminant();

        if (MathUtilities.IsZero(determinant))
        {
            return false;
        }

        float m11 = matrix.M11;
        float m21 = matrix.M21;
        float m12 = matrix.M12;
        float m22 = matrix.M22;

        // Translation.
        decomposed.Translate = new Vector(matrix.M31, matrix.M32);

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

        decomposed.Scale = new Vector(scaleX, scaleY);
        decomposed.Skew = new Vector(scaledShear / scaleY, 0f);
        decomposed.Angle = MathF.Atan2(m12, m11);

        return true;
    }

    public struct Decomposed
    {
        public Vector Translate;
        public Vector Scale;
        public Vector Skew;
        public float Angle;
    }
}
