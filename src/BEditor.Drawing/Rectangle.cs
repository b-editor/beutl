// Rectangle.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Runtime.Serialization;

using BEditor.Drawing.Resources;

namespace BEditor.Drawing
{
    /// <summary>
    /// Stores a set of four integers that represent the location and size of a rectangle.
    /// </summary>
    [Serializable]
    public readonly struct Rectangle : IEquatable<Rectangle>, ISerializable
    {
        /// <summary>
        /// Represents a <see cref="Rectangle"/> structure with its properties left uninitialized.
        /// </summary>
        public static readonly Rectangle Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="Rectangle"/> struct.
        /// </summary>
        /// <param name="x">The x-coordinate of the upper-left corner of the <see cref="Rectangle"/>.</param>
        /// <param name="y">The y-coordinate of the upper-left corner of the <see cref="Rectangle"/>.</param>
        /// <param name="width">The width of the <see cref="Rectangle"/>.</param>
        /// <param name="height">The height of the <see cref="Rectangle"/>.</param>
        public Rectangle(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Rectangle"/> struct.
        /// </summary>
        /// <param name="point">A <see cref="Drawing.Point"/> that represents the upper-left corner of the rectangular region.</param>
        /// <param name="size">A <see cref="Drawing.Size"/> that represents the width and height of the rectangular region.</param>
        public Rectangle(Point point, Size size)
        {
            X = point.X;
            Y = point.Y;
            Width = size.Width;
            Height = size.Height;
        }

        /// <summary>
        /// Gets the x-coordinate of the upper-left corner of this <see cref="Rectangle"/>.
        /// </summary>
        public int X { get; }

        /// <summary>
        /// Gets the y-coordinate of the upper-left corner of this <see cref="Rectangle"/>.
        /// </summary>
        public int Y { get; }

        /// <summary>
        /// Gets the width of thie <see cref="Rectangle"/>.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the height of thie <see cref="Rectangle"/>.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets the y-coordinate of the top edge of this <see cref="Rectangle"/>.
        /// </summary>
        public int Top => Y;

        /// <summary>
        /// Gets the y-coordinate that is the sum of the <see cref="Y"/> and <see cref="Height"/> property values of this <see cref="Rectangle"/>.
        /// </summary>
        public int Bottom => Y + Height;

        /// <summary>
        /// Gets the x-coordinate of the left edge of this <see cref="Rectangle"/>.
        /// </summary>
        public int Left => X;

        /// <summary>
        /// Gets the x-coordinate that is the sum of <see cref="X"/> and <see cref="Width"/> property values of this <see cref="Rectangle"/>.
        /// </summary>
        public int Right => X + Width;

        /// <summary>
        /// Gets the upper left point of this <see cref="Rectangle"/>.
        /// </summary>
        public Point TopLeft => new(X, Y);

        /// <summary>
        /// Gets the lower right point of this <see cref="Rectangle"/>.
        /// </summary>
        public Point BottomRight => new(X + Width, Y + Height);

        /// <summary>
        /// Gets the coordinates of the upper-left corner of this <see cref="Rectangle"/>.
        /// </summary>
        public Point Point => new(X, Y);

        /// <summary>
        /// Gets the size of this <see cref="Rectangle"/>.
        /// </summary>
        public Size Size => new(Width, Height);

        /// <summary>
        /// Adds the specified <see cref="Drawing.Point"/> to the specified <see cref="Rectangle"/>.
        /// </summary>
        /// <param name="rect">The <see cref="Rectangle"/> to add.</param>
        /// <param name="point">The <see cref="Drawing.Point"/> to add.</param>
        /// <returns>The <see cref="Rectangle"/> that is the result of the addition operation.</returns>
        public static Rectangle operator +(Rectangle rect, Point point)
        {
            return new(rect.X + point.X, rect.Y + point.Y, rect.Width, rect.Height);
        }

        /// <summary>
        /// Adds the specified <see cref="Drawing.Size"/> to the specified <see cref="Rectangle"/>.
        /// </summary>
        /// <param name="rect">The <see cref="Rectangle"/> to add.</param>
        /// <param name="size">The <see cref="Drawing.Size"/> to add.</param>
        /// <returns>The <see cref="Rectangle"/> that is the result of the addition operation.</returns>
        public static Rectangle operator +(Rectangle rect, Size size)
        {
            return new(rect.X, rect.Y, rect.Width + size.Width, rect.Height + size.Height);
        }

        /// <summary>
        /// Returns the result of subtracting specified <see cref="Drawing.Point"/> from the specified <see cref="Rectangle"/>.
        /// </summary>
        /// <param name="rect">The <see cref="Rectangle"/> to be subtracted from.</param>
        /// <param name="point">The <see cref="Drawing.Point"/> to subtract from the <see cref="Rectangle"/>.</param>
        /// <returns>The <see cref="Rectangle"/> that is the result of the subtraction operation.</returns>
        public static Rectangle operator -(Rectangle rect, Point point)
        {
            return new(rect.X - point.X, rect.Y - point.Y, rect.Width, rect.Height);
        }

        /// <summary>
        /// Returns the result of subtracting specified <see cref="Drawing.Size"/> from the specified <see cref="Rectangle"/>.
        /// </summary>
        /// <param name="rect">The <see cref="Rectangle"/> to be subtracted from.</param>
        /// <param name="size">The <see cref="Drawing.Size"/> to subtract from the <see cref="Rectangle"/>.</param>
        /// <returns>The <see cref="Rectangle"/> that is the result of the subtraction operation.</returns>
        public static Rectangle operator -(Rectangle rect, Size size)
        {
            return new(rect.X, rect.Y, rect.Width - size.Width, rect.Height - size.Height);
        }

        /// <summary>
        /// Compares two <see cref="Rectangle"/>. The result specifies whether the values of the <see cref="Point"/> and <see cref="Size"/> properties of the two <see cref="Rectangle"/> are equal.
        /// </summary>
        /// <param name="left">A <see cref="Rectangle"/> to compare.</param>
        /// <param name="right">A <see cref="Rectangle"/> to compare.</param>
        /// <returns>true if the <see cref="Point"/> and <see cref="Size"/> values of the left and right <see cref="Rectangle"/> structures are equal; otherwise, false.</returns>
        public static bool operator ==(Rectangle left, Rectangle right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether the coordinates of the specified points are not equal.
        /// </summary>
        /// <param name="left">A <see cref="Rectangle"/> to compare.</param>
        /// <param name="right">A <see cref="Rectangle"/> to compare.</param>
        /// <returns>true if the <see cref="Point"/> and <see cref="Size"/> values of the left and right <see cref="Rectangle"/> differ; otherwise, false.</returns>
        public static bool operator !=(Rectangle left, Rectangle right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Creates a <see cref="Rectangle"/> with the specified edge locations.
        /// </summary>
        /// <param name="left">The x-coordinate of the upper-left corner of this <see cref="Rectangle"/>.</param>
        /// <param name="top">The y-coordinate of the upper-left corner of this <see cref="Rectangle"/>.</param>
        /// <param name="right">The x-coordinate of the lower-right corner of this <see cref="Rectangle"/>.</param>
        /// <param name="bottom">The y-coordinate of the lower-right corner of this <see cref="Rectangle"/>.</param>
        /// <returns>The new <see cref="Rectangle"/> that this method creates.</returns>
        public static Rectangle FromLTRB(int left, int top, int right, int bottom)
        {
            var r = new Rectangle(
                x: left,
                y: top,
                width: right - left,
                height: bottom - top);

            if (r.Width < 0) throw new ArgumentException(string.Format(Strings.LessThan, nameof(left), nameof(right)));

            if (r.Height < 0) throw new ArgumentException(string.Format(Strings.LessThan, nameof(top), nameof(bottom)));

            return r;
        }

        /// <summary>
        /// Creates and returns an enlarged copy of the specified <see cref="Rectangle"/>. The copy is enlarged by the specified amount. The original <see cref="Rectangle"/> remains unmodified.
        /// </summary>
        /// <param name="rect">The <see cref="Rectangle"/> with which to start. This rectangle is not modified.</param>
        /// <param name="x">The amount to inflate this <see cref="Rectangle"/> horizontally.</param>
        /// <param name="y">The amount to inflate this <see cref="Rectangle"/> vertically.</param>
        /// <returns>The enlarged <see cref="Rectangle"/>.</returns>
        public static Rectangle Inflate(Rectangle rect, int x, int y)
        {
            return new(
                rect.X - x,
                rect.Y - y,
                rect.Width + (2 * x),
                rect.Height + (2 * y));
        }

        /// <summary>
        /// Returns a third <see cref="Rectangle"/> that represents the intersection of two other <see cref="Rectangle"/>. If there is no intersection, an empty <see cref="Rectangle"/> is returned.
        /// </summary>
        /// <param name="a"> A rectangle to intersect.</param>
        /// <param name="b"> A rectangle to intersect.</param>
        /// <returns>A <see cref="Rectangle"/> that represents the intersection of <paramref name="a"/> and <paramref name="b"/>.</returns>
        public static Rectangle Intersect(Rectangle a, Rectangle b)
        {
            var x1 = Math.Max(a.X, b.X);
            var x2 = Math.Min(a.X + a.Width, b.X + b.Width);
            var y1 = Math.Max(a.Y, b.Y);
            var y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

            if (x2 >= x1 && y2 >= y1)
            {
                return new Rectangle(x1, y1, x2 - x1, y2 - y1);
            }

            return Empty;
        }

        /// <summary>
        /// Gets a <see cref="Rectangle"/> that contains the union of two <see cref="Rectangle"/>.
        /// </summary>
        /// <param name="a">A rectangle to union.</param>
        /// <param name="b">A rectangle to union.</param>
        /// <returns>A <see cref="Rectangle"/> that bounds the union of the two <see cref="Rectangle"/>.</returns>
        public static Rectangle Union(Rectangle a, Rectangle b)
        {
            var x1 = Math.Min(a.X, b.X);
            var x2 = Math.Max(a.X + a.Width, b.X + b.Width);
            var y1 = Math.Min(a.Y, b.Y);
            var y2 = Math.Max(a.Y + a.Height, b.Y + b.Height);

            return new Rectangle(x1, y1, x2 - x1, y2 - y1);
        }

        /// <inheritdoc/>
        public bool Equals(Rectangle other)
        {
            return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is Rectangle other && Equals(other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Width, Height);
        }

        /// <inheritdoc/>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(X), X);
            info.AddValue(nameof(Y), Y);
            info.AddValue(nameof(Width), Width);
            info.AddValue(nameof(Height), Height);
        }
    }
}