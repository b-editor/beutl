// Point.cs
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
    public readonly struct Point : IEquatable<Point>, ISerializable
    {
        /// <summary>
        /// Represents a <see cref="Point"/> that has <see cref="X"/> and <see cref="Y"/> values set to zero.
        /// </summary>
        public static readonly Point Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="Point"/> struct.
        /// </summary>
        /// <param name="x">The horizontal position of the point.</param>
        /// <param name="y">The vertical position of the point.</param>
        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        private Point(SerializationInfo info, StreamingContext context)
        {
            X = info.GetInt32(nameof(X));
            Y = info.GetInt32(nameof(Y));
        }

        /// <summary>
        /// Gets the x-coordinate of this <see cref="Point"/>.
        /// </summary>
        public int X { get; }

        /// <summary>
        /// Gets the y-coordinate of this <see cref="Point"/>.
        /// </summary>
        public int Y { get; }

        /// <summary>
        /// Compares two <see cref="Point"/> structures. The result specifies whether the values of the <see cref="X"/> and <see cref="Y"/> properties of the two <see cref="Point"/> structures are equal.
        /// </summary>
        /// <param name="left">A <see cref="Point"/> to compare.</param>
        /// <param name="right">A <see cref="Point"/> to compare.</param>
        /// <returns>true if the <see cref="X"/> and <see cref="Y"/> values of the left and right <see cref="Point"/> structures are equal; otherwise, false.</returns>
        public static bool operator ==(Point left, Point right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether the coordinates of the specified points are not equal.
        /// </summary>
        /// <param name="left">A <see cref="Point"/> to compare.</param>
        /// <param name="right">A <see cref="Point"/> to compare.</param>
        /// <returns>true if the <see cref="X"/> and <see cref="Y"/> values of the left and right <see cref="Point"/> structures differ; otherwise, false.</returns>
        public static bool operator !=(Point left, Point right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Adds the specified <see cref="Point"/> to the specified <see cref="Point"/>.
        /// </summary>
        /// <param name="point1">The <see cref="Point"/> to add.</param>
        /// <param name="point2">The <see cref="Point"/> to add to <paramref name="point1"/>.</param>
        /// <returns>The <see cref="Point"/> that is the result of the addition operation.</returns>
        public static Point operator +(Point point1, Point point2)
        {
            return Add(point1, point2);
        }

        /// <summary>
        /// Adds the specified <see cref="Size"/> to the specified <see cref="Point"/>.
        /// </summary>
        /// <param name="point">The <see cref="Point"/> to add.</param>
        /// <param name="size">The <see cref="Size"/> to add.</param>
        /// <returns>The <see cref="Point"/> that is the result of the addition operation.</returns>
        public static Point operator +(Point point, Size size)
        {
            return Add(point, size);
        }

        /// <summary>
        /// Returns the result of subtracting specified <see cref="Point"/> from the specified <see cref="Point"/>.
        /// </summary>
        /// <param name="point1">The <see cref="Point"/> to be subtracted from.</param>
        /// <param name="point2">The <see cref="Point"/> to subtract from the <see cref="Point"/>.</param>
        /// <returns>The <see cref="Point"/> that is the result of the subtraction operation.</returns>
        public static Point operator -(Point point1, Point point2)
        {
            return Subtract(point1, point2);
        }

        /// <summary>
        /// Returns the result of subtracting specified <see cref="Size"/> from the specified <see cref="Point"/>.
        /// </summary>
        /// <param name="point">The <see cref="Point"/> to be subtracted from.</param>
        /// <param name="size">The <see cref="Size"/> to subtract from the <see cref="Point"/>.</param>
        /// <returns>The <see cref="Point"/> that is the result of the subtraction operation.</returns>
        public static Point operator -(Point point, Size size)
        {
            return Subtract(point, size);
        }

        /// <summary>
        /// Adds the specified <see cref="Size"/> to the specified <see cref="Point"/>.
        /// </summary>
        /// <param name="point">The <see cref="Point"/> to add.</param>
        /// <param name="size">The <see cref="Size"/> to add.</param>
        /// <returns>The <see cref="Point"/> that is the result of the addition operation.</returns>
        public static Point Add(Point point, Size size)
        {
            return new(point.X + size.Width, point.Y + size.Height);
        }

        /// <summary>
        /// Adds the specified <see cref="Point"/> to the specified <see cref="Point"/>.
        /// </summary>
        /// <param name="point1">The <see cref="Point"/> to add.</param>
        /// <param name="point2">The <see cref="Point"/> to add to <paramref name="point1"/>.</param>
        /// <returns>The <see cref="Point"/> that is the result of the addition operation.</returns>
        public static Point Add(Point point1, Point point2)
        {
            return new(point1.X + point2.X, point1.Y + point2.Y);
        }

        /// <summary>
        /// Returns the result of subtracting specified <see cref="Size"/> from the specified <see cref="Point"/>.
        /// </summary>
        /// <param name="point">The <see cref="Point"/> to be subtracted from.</param>
        /// <param name="size">The <see cref="Size"/> to subtract from the <see cref="Point"/>.</param>
        /// <returns>The <see cref="Point"/> that is the result of the subtraction operation.</returns>
        public static Point Subtract(Point point, Size size)
        {
            return new(point.X - size.Width, point.Y - size.Height);
        }

        /// <summary>
        /// Returns the result of subtracting specified <see cref="Point"/> from the specified <see cref="Point"/>.
        /// </summary>
        /// <param name="point1">The <see cref="Point"/> to be subtracted from.</param>
        /// <param name="point2">The <see cref="Point"/> to subtract from the <see cref="Point"/>.</param>
        /// <returns>The <see cref="Point"/> that is the result of the subtraction operation.</returns>
        public static Point Subtract(Point point1, Point point2)
        {
            return new(point1.X - point2.X, point1.Y - point2.Y);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is Point point && Equals(point);
        }

        /// <inheritdoc/>
        public bool Equals(Point other)
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