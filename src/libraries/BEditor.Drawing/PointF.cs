// PointF.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Runtime.Serialization;

namespace BEditor.Drawing
{
    /// <summary>
    /// Represents a pair of integer x- and y-coordinates.
    /// </summary>
    [Serializable]
    public readonly struct PointF : IEquatable<PointF>, ISerializable
    {
        /// <summary>
        /// Represents a <see cref="PointF"/> that has <see cref="X"/> and <see cref="Y"/> values set to zero.
        /// </summary>
        public static readonly PointF Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="PointF"/> struct.
        /// </summary>
        /// <param name="x">The horizontal position of the point.</param>
        /// <param name="y">The vertical position of the point.</param>
        public PointF(float x, float y)
        {
            X = x;
            Y = y;
        }

        private PointF(SerializationInfo info, StreamingContext context)
        {
            X = info.GetSingle(nameof(X));
            Y = info.GetSingle(nameof(Y));
        }

        /// <summary>
        /// Gets the x-coordinate of this <see cref="PointF"/>.
        /// </summary>
        public float X { get; }

        /// <summary>
        /// Gets the x-coordinate of this <see cref="PointF"/>.
        /// </summary>
        public float Y { get; }

        /// <summary>
        /// Compares two <see cref="PointF"/> structures. The result specifies whether the values of the <see cref="X"/> and <see cref="Y"/> properties of the two <see cref="PointF"/> structures are equal.
        /// </summary>
        /// <param name="left">A <see cref="PointF"/> to compare.</param>
        /// <param name="right">A <see cref="PointF"/> to compare.</param>
        /// <returns>true if the <see cref="X"/> and <see cref="Y"/> values of the left and right <see cref="PointF"/> structures are equal; otherwise, false.</returns>
        public static bool operator ==(PointF left, PointF right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether the coordinates of the specified points are not equal.
        /// </summary>
        /// <param name="left">A <see cref="PointF"/> to compare.</param>
        /// <param name="right">A <see cref="PointF"/> to compare.</param>
        /// <returns>true if the <see cref="X"/> and <see cref="Y"/> values of the left and right <see cref="PointF"/> structures differ; otherwise, false.</returns>
        public static bool operator !=(PointF left, PointF right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Adds the specified <see cref="PointF"/> to the specified <see cref="PointF"/>.
        /// </summary>
        /// <param name="point1">The <see cref="PointF"/> to add.</param>
        /// <param name="point2">The <see cref="PointF"/> to add to <paramref name="point1"/>.</param>
        /// <returns>The <see cref="PointF"/> that is the result of the addition operation.</returns>
        public static PointF operator +(PointF point1, PointF point2)
        {
            return Add(point1, point2);
        }

        /// <summary>
        /// Adds the specified <see cref="Size"/> to the specified <see cref="PointF"/>.
        /// </summary>
        /// <param name="point">The <see cref="PointF"/> to add.</param>
        /// <param name="size">The <see cref="Size"/> to add.</param>
        /// <returns>The <see cref="PointF"/> that is the result of the addition operation.</returns>
        public static PointF operator +(PointF point, Size size)
        {
            return Add(point, size);
        }

        /// <summary>
        /// Returns the result of subtracting specified <see cref="PointF"/> from the specified <see cref="PointF"/>.
        /// </summary>
        /// <param name="point1">The <see cref="PointF"/> to be subtracted from.</param>
        /// <param name="point2">The <see cref="PointF"/> to subtract from the <see cref="PointF"/>.</param>
        /// <returns>The <see cref="PointF"/> that is the result of the subtraction operation.</returns>
        public static PointF operator -(PointF point1, PointF point2)
        {
            return Subtract(point1, point2);
        }

        /// <summary>
        /// Returns the result of subtracting specified <see cref="Size"/> from the specified <see cref="PointF"/>.
        /// </summary>
        /// <param name="point">The <see cref="PointF"/> to be subtracted from.</param>
        /// <param name="size">The <see cref="Size"/> to subtract from the <see cref="PointF"/>.</param>
        /// <returns>The <see cref="PointF"/> that is the result of the subtraction operation.</returns>
        public static PointF operator -(PointF point, Size size)
        {
            return Subtract(point, size);
        }

        /// <summary>
        /// Adds the specified <see cref="Size"/> to the specified <see cref="PointF"/>.
        /// </summary>
        /// <param name="point">The <see cref="PointF"/> to add.</param>
        /// <param name="size">The <see cref="Size"/> to add.</param>
        /// <returns>The <see cref="PointF"/> that is the result of the addition operation.</returns>
        public static PointF Add(PointF point, Size size)
        {
            return new(point.X + size.Width, point.Y + size.Height);
        }

        /// <summary>
        /// Adds the specified <see cref="PointF"/> to the specified <see cref="PointF"/>.
        /// </summary>
        /// <param name="point1">The <see cref="PointF"/> to add.</param>
        /// <param name="point2">The <see cref="PointF"/> to add to <paramref name="point1"/>.</param>
        /// <returns>The <see cref="PointF"/> that is the result of the addition operation.</returns>
        public static PointF Add(PointF point1, PointF point2)
        {
            return new(point1.X + point2.X, point1.Y + point2.Y);
        }

        /// <summary>
        /// Returns the result of subtracting specified <see cref="Size"/> from the specified <see cref="PointF"/>.
        /// </summary>
        /// <param name="point">The <see cref="PointF"/> to be subtracted from.</param>
        /// <param name="size">The <see cref="Size"/> to subtract from the <see cref="PointF"/>.</param>
        /// <returns>The <see cref="PointF"/> that is the result of the subtraction operation.</returns>
        public static PointF Subtract(PointF point, Size size)
        {
            return new(point.X - size.Width, point.Y - size.Height);
        }

        /// <summary>
        /// Returns the result of subtracting specified <see cref="PointF"/> from the specified <see cref="PointF"/>.
        /// </summary>
        /// <param name="point1">The <see cref="PointF"/> to be subtracted from.</param>
        /// <param name="point2">The <see cref="PointF"/> to subtract from the <see cref="PointF"/>.</param>
        /// <returns>The <see cref="PointF"/> that is the result of the subtraction operation.</returns>
        public static PointF Subtract(PointF point1, PointF point2)
        {
            return new(point1.X - point2.X, point1.Y - point2.Y);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is PointF point && Equals(point);
        }

        /// <inheritdoc/>
        public bool Equals(PointF other)
        {
            return X == other.X && Y == other.Y;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y);
        }

        /// <inheritdoc/>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(X), X);
            info.AddValue(nameof(Y), Y);
        }
    }
}