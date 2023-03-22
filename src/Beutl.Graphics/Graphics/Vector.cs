using System.Globalization;
using System.Text.Json.Serialization;

using Beutl.Converters;
using Beutl.Utilities;
using Beutl.Validation;

namespace Beutl.Graphics;

/// <summary>
/// Defines a vector.
/// </summary>
[JsonConverter(typeof(VectorJsonConverter))]
public readonly struct Vector : IEquatable<Vector>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Vector"/> structure.
    /// </summary>
    /// <param name="x">The X component.</param>
    /// <param name="y">The Y component.</param>
    public Vector(float x, float y)
    {
        X = x;
        Y = y;
    }

    /// <summary>
    /// Gets the X component.
    /// </summary>
    public float X { get; }

    /// <summary>
    /// Gets the Y component.
    /// </summary>
    public float Y { get; }

    /// <summary>
    /// Returns the vector (0.0, 0.0).
    /// </summary>
    public static Vector Zero => new(0, 0);

    /// <summary>
    /// Returns the vector (1.0, 1.0).
    /// </summary>
    public static Vector One => new(1, 1);

    /// <summary>
    /// Returns the vector (1.0, 0.0).
    /// </summary>
    public static Vector UnitX => new(1, 0);

    /// <summary>
    /// Returns the vector (0.0, 1.0).
    /// </summary>
    public static Vector UnitY => new(0, 1);

    /// <summary>
    /// Gets a value indicating whether the X and Y components are zero.
    /// </summary>
    public bool IsDefault => (X == 0) && (Y == 0);

    /// <summary>
    /// Converts the <see cref="Vector"/> to a <see cref="Point"/>.
    /// </summary>
    /// <param name="a">The vector.</param>
    public static explicit operator Point(Vector a)
    {
        return new Point(a.X, a.Y);
    }

    /// <summary>
    /// Calculates the dot product of two vectors.
    /// </summary>
    /// <param name="a">First vector.</param>
    /// <param name="b">Second vector.</param>
    /// <returns>The dot product.</returns>
    public static float operator *(Vector a, Vector b)
    {
        return Dot(a, b);
    }

    /// <summary>
    /// Scales a vector.
    /// </summary>
    /// <param name="vector">The vector.</param>
    /// <param name="scale">The scaling factor.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector operator *(Vector vector, float scale)
    {
        return Multiply(vector, scale);
    }

    /// <summary>
    /// Scales a vector.
    /// </summary>
    /// <param name="vector">The vector.</param>
    /// <param name="scale">The scaling factor.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector operator *(float scale, Vector vector)
    {
        return Multiply(vector, scale);
    }

    /// <summary>
    /// Scales a vector.
    /// </summary>
    /// <param name="vector">The vector.</param>
    /// <param name="scale">The divisor.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector operator /(Vector vector, float scale)
    {
        return Divide(vector, scale);
    }

    /// <summary>
    /// Parses a <see cref="Vector"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="vector">The <see cref="Vector"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(string s, out Vector vector)
    {
        return TryParse(s.AsSpan(), out vector);
    }

    /// <summary>
    /// Parses a <see cref="Vector"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <param name="vector">The <see cref="Vector"/>.</param>
    /// <returns>The status of the operation.</returns>
    public static bool TryParse(ReadOnlySpan<char> s, out Vector vector)
    {
        try
        {
            vector = Parse(s);
            return true;
        }
        catch
        {
            vector = default;
            return false;
        }
    }

    /// <summary>
    /// Parses a <see cref="Vector"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The <see cref="Vector"/>.</returns>
    public static Vector Parse(string s)
    {
        return Parse(s.AsSpan());
    }
    
    /// <summary>
    /// Parses a <see cref="Vector"/> string.
    /// </summary>
    /// <param name="s">The string.</param>
    /// <returns>The <see cref="Vector"/>.</returns>
    public static Vector Parse(ReadOnlySpan<char> s)
    {
        using (var tokenizer = new RefStringTokenizer(s, CultureInfo.InvariantCulture, exceptionMessage: "Invalid Vector."))
        {
            return new Vector(
                tokenizer.ReadSingle(),
                tokenizer.ReadSingle()
            );
        }
    }

    /// <summary>
    /// Length of the vector.
    /// </summary>
    public float Length => MathF.Sqrt(SquaredLength);

    /// <summary>
    /// Squared Length of the vector.
    /// </summary>
    public float SquaredLength => X * X + Y * Y;

    /// <summary>
    /// Negates a vector.
    /// </summary>
    /// <param name="a">The vector.</param>
    /// <returns>The negated vector.</returns>
    public static Vector operator -(Vector a)
    {
        return Negate(a);
    }

    /// <summary>
    /// Adds two vectors.
    /// </summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>A vector that is the result of the addition.</returns>
    public static Vector operator +(Vector a, Vector b)
    {
        return Add(a, b);
    }

    /// <summary>
    /// Subtracts two vectors.
    /// </summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>A vector that is the result of the subtraction.</returns>
    public static Vector operator -(Vector a, Vector b)
    {
        return Subtract(a, b);
    }

    public static bool operator ==(Vector left, Vector right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(Vector left, Vector right)
    {
        return !left.Equals(right);
    }

    /// <summary>
    /// Check if two vectors are equal (bitwise).
    /// </summary>
    /// <param name="other"></param>
    /// <returns></returns>
    public bool Equals(Vector other)
    {
        return X == other.X && Y == other.Y;
    }

    /// <summary>
    /// Check if two vectors are nearly equal (numerically).
    /// </summary>
    /// <param name="other">The other vector.</param>
    /// <returns>True if vectors are nearly equal.</returns>
    public bool NearlyEquals(Vector other)
    {
        return MathUtilities.AreClose(X, other.X) &&
               MathUtilities.AreClose(Y, other.Y);
    }

    public override bool Equals(object? obj)
    {
        return obj is Vector other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (X.GetHashCode() * 397) ^ Y.GetHashCode();
        }
    }

    /// <summary>
    /// Returns the string representation of the vector.
    /// </summary>
    /// <returns>The string representation of the vector.</returns>
    public override string ToString()
    {
        return FormattableString.Invariant($"{X}, {Y}");
    }

    /// <summary>
    /// Returns a new vector with the specified X component.
    /// </summary>
    /// <param name="x">The X component.</param>
    /// <returns>The new vector.</returns>
    public Vector WithX(float x)
    {
        return new Vector(x, Y);
    }

    /// <summary>
    /// Returns a new vector with the specified Y component.
    /// </summary>
    /// <param name="y">The Y component.</param>
    /// <returns>The new vector.</returns>
    public Vector WithY(float y)
    {
        return new Vector(X, y);
    }

    /// <summary>
    /// Returns a normalized version of this vector.
    /// </summary>
    /// <returns>The normalized vector.</returns>
    public Vector Normalize()
    {
        return Normalize(this);
    }

    /// <summary>
    /// Returns a negated version of this vector.
    /// </summary>
    /// <returns>The negated vector.</returns>
    public Vector Negate()
    {
        return Negate(this);
    }

    /// <summary>
    /// Returns the dot product of two vectors.
    /// </summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The dot product.</returns>
    public static float Dot(Vector a, Vector b)
    {
        return a.X * b.X + a.Y * b.Y;
    }

    /// <summary>
    /// Returns the cross product of two vectors.
    /// </summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The cross product.</returns>
    public static float Cross(Vector a, Vector b)
    {
        return a.X * b.Y - a.Y * b.X;
    }

    /// <summary>
    /// Normalizes the given vector.
    /// </summary>
    /// <param name="vector">The vector</param>
    /// <returns>The normalized vector.</returns>
    public static Vector Normalize(Vector vector)
    {
        return Divide(vector, vector.Length);
    }

    /// <summary>
    /// Divides the first vector by the second.
    /// </summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector Divide(Vector a, Vector b)
    {
        return new Vector(a.X / b.X, a.Y / b.Y);
    }

    /// <summary>
    /// Divides the vector by the given scalar.
    /// </summary>
    /// <param name="vector">The vector</param>
    /// <param name="scalar">The scalar value</param>
    /// <returns>The scaled vector.</returns>
    public static Vector Divide(Vector vector, float scalar)
    {
        return new Vector(vector.X / scalar, vector.Y / scalar);
    }

    /// <summary>
    /// Multiplies the first vector by the second.
    /// </summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector Multiply(Vector a, Vector b)
    {
        return new Vector(a.X * b.X, a.Y * b.Y);
    }

    /// <summary>
    /// Multiplies the vector by the given scalar.
    /// </summary>
    /// <param name="vector">The vector</param>
    /// <param name="scalar">The scalar value</param>
    /// <returns>The scaled vector.</returns>
    public static Vector Multiply(Vector vector, float scalar)
    {
        return new Vector(vector.X * scalar, vector.Y * scalar);
    }

    /// <summary>
    /// Adds the second to the first vector
    /// </summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The summed vector.</returns>
    public static Vector Add(Vector a, Vector b)
    {
        return new Vector(a.X + b.X, a.Y + b.Y);
    }

    /// <summary>
    /// Subtracts the second from the first vector
    /// </summary>
    /// <param name="a">The first vector.</param>
    /// <param name="b">The second vector.</param>
    /// <returns>The difference vector.</returns>
    public static Vector Subtract(Vector a, Vector b)
    {
        return new Vector(a.X - b.X, a.Y - b.Y);
    }

    /// <summary>
    /// Negates the vector
    /// </summary>
    /// <param name="vector">The vector to negate.</param>
    /// <returns>The scaled vector.</returns>
    public static Vector Negate(Vector vector)
    {
        return new Vector(-vector.X, -vector.Y);
    }

    /// <summary>
    /// Deconstructs the vector into its X and Y components.
    /// </summary>
    /// <param name="x">The X component.</param>
    /// <param name="y">The Y component.</param>
    public void Deconstruct(out float x, out float y)
    {
        x = X;
        y = Y;
    }
}
