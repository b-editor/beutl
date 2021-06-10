// Point3.cs
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
    /// Represents a pair of integer x- and y- and z-coordinates.
    /// </summary>
    [Serializable]
    public readonly struct Point3 : IEquatable<Point3>, ISerializable
    {
        /// <summary>
        /// Represents a <see cref="Point3"/> that has <see cref="X"/> and <see cref="Y"/> and <see cref="Z"/> values set to zero.
        /// </summary>
        public static readonly Point3 Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="Point3"/> struct.
        /// </summary>
        /// <param name="x">The x-coordinate of the <see cref="Point3"/>.</param>
        /// <param name="y">The y-coordinate of the <see cref="Point3"/>.</param>
        /// <param name="z">The z-coordinate of the <see cref="Point3"/>.</param>
        public Point3(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        private Point3(SerializationInfo info, StreamingContext context)
        {
            X = info.GetInt32(nameof(X));
            Y = info.GetInt32(nameof(Y));
            Z = info.GetInt32(nameof(Z));
        }

        /// <summary>
        /// Gets the x-coordinate of this <see cref="Point3"/>.
        /// </summary>
        public int X { get; }

        /// <summary>
        /// Gets the y-coordinate of this <see cref="Point3"/>.
        /// </summary>
        public int Y { get; }

        /// <summary>
        /// Gets the z-coordinate of this <see cref="Point3"/>.
        /// </summary>
        public int Z { get; }

        /// <summary>
        /// Adds the specified <see cref="Point3"/> to the specified <see cref="Point3"/>.
        /// </summary>
        /// <param name="point1">The <see cref="Point3"/> to add.</param>
        /// <param name="point2">The <see cref="Point3"/> to add to <paramref name="point1"/>.</param>
        /// <returns>The <see cref="Point3"/> that is the result of the addition operation.</returns>
        public static Point3 operator +(Point3 point1, Point3 point2)
        {
            return Add(point1, point2);
        }

        /// <summary>
        /// Returns the result of subtracting specified <see cref="Point3"/> from the specified <see cref="Point3"/>.
        /// </summary>
        /// <param name="point1">The <see cref="Point3"/> to be subtracted from.</param>
        /// <param name="point2">The <see cref="Point3"/> to subtract from the <see cref="Point3"/>.</param>
        /// <returns>The <see cref="Point3"/> that is the result of the subtraction operation.</returns>
        public static Point3 operator -(Point3 point1, Point3 point2)
        {
            return Subtract(point1, point2);
        }

        /// <summary>
        /// Compares two <see cref="Point3"/> structures. The result specifies whether the values of the <see cref="X"/> and <see cref="Y"/> and <see cref="Z"/> properties of the two <see cref="Point3"/> structures are equal.
        /// </summary>
        /// <param name="left">A <see cref="Point3"/> to compare.</param>
        /// <param name="right">A <see cref="Point3"/> to compare.</param>
        /// <returns>true if the <see cref="X"/> and <see cref="Y"/> and <see cref="Z"/> values of the left and right <see cref="Point3"/> structures are equal; otherwise, false.</returns>
        public static bool operator ==(Point3 left, Point3 right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether the coordinates of the specified points are not equal.
        /// </summary>
        /// <param name="left">A <see cref="Point3"/> to compare.</param>
        /// <param name="right">A <see cref="Point3"/> to compare.</param>
        /// <returns>true if the <see cref="X"/> and <see cref="Y"/> and <see cref="Z"/> values of the left and right <see cref="Point3"/> structures differ; otherwise, false.</returns>
        public static bool operator !=(Point3 left, Point3 right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Adds the specified <see cref="Point3"/> to the specified <see cref="Point3"/>.
        /// </summary>
        /// <param name="point1">The <see cref="Point3"/> to add.</param>
        /// <param name="point2">The <see cref="Point3"/> to add to <paramref name="point1"/>.</param>
        /// <returns>The <see cref="Point3"/> that is the result of the addition operation.</returns>
        public static Point3 Add(Point3 point1, Point3 point2)
        {
            return new(point1.X + point2.X, point1.Y + point2.Y, point1.Z + point2.Z);
        }

        /// <summary>
        /// Returns the result of subtracting specified <see cref="Point3"/> from the specified <see cref="Point3"/>.
        /// </summary>
        /// <param name="point1">The <see cref="Point3"/> to be subtracted from.</param>
        /// <param name="point2">The <see cref="Point3"/> to subtract from the <see cref="Point3"/>.</param>
        /// <returns>The <see cref="Point3"/> that is the result of the subtraction operation.</returns>
        public static Point3 Subtract(Point3 point1, Point3 point2)
        {
            return new(point1.X - point2.X, point1.Y - point2.Y, point1.Z - point2.Z);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is Point3 point && Equals(point);
        }

        /// <inheritdoc/>
        public bool Equals(Point3 other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Z);
        }

        /// <inheritdoc/>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(X), X);
            info.AddValue(nameof(Y), Y);
            info.AddValue(nameof(Z), Z);
        }
    }
}