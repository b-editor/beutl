// RectangleF.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Runtime.Serialization;

using BEditor.LangResources;

namespace BEditor.Drawing
{
    /// <summary>
    /// Stores a set of four integers that represent the location and size of a rectangle.
    /// </summary>
    [Serializable]
    public readonly struct RectangleF : IEquatable<RectangleF>, ISerializable
    {
        /// <summary>
        /// Represents a <see cref="RectangleF"/> structure with its properties left uninitialized.
        /// </summary>
        public static readonly RectangleF Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="RectangleF"/> struct.
        /// </summary>
        /// <param name="x">The x-coordinate of the upper-left corner of the <see cref="RectangleF"/>.</param>
        /// <param name="y">The y-coordinate of the upper-left corner of the <see cref="RectangleF"/>.</param>
        /// <param name="width">The width of the <see cref="RectangleF"/>.</param>
        /// <param name="height">The height of the <see cref="RectangleF"/>.</param>
        public RectangleF(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RectangleF"/> struct.
        /// </summary>
        /// <param name="point">A <see cref="PointF"/> that represents the upper-left corner of the rectangular region.</param>
        /// <param name="size">A <see cref="SizeF"/> that represents the width and height of the rectangular region.</param>
        public RectangleF(PointF point, SizeF size)
        {
            X = point.X;
            Y = point.Y;
            Width = size.Width;
            Height = size.Height;
        }

        /// <summary>
        /// Gets the x-coordinate of the upper-left corner of this <see cref="RectangleF"/>.
        /// </summary>
        public float X { get; }

        /// <summary>
        /// Gets the y-coordinate of the upper-left corner of this <see cref="RectangleF"/>.
        /// </summary>
        public float Y { get; }

        /// <summary>
        /// Gets the width of thie <see cref="Rectangle"/>.
        /// </summary>
        public float Width { get; }

        /// <summary>
        /// Gets the height of thie <see cref="Rectangle"/>.
        /// </summary>
        public float Height { get; }

        /// <summary>
        /// Gets the y-coordinate of the top edge of this <see cref="Rectangle"/>.
        /// </summary>
        public float Top => Y;

        /// <summary>
        /// Gets the y-coordinate that is the sum of the <see cref="Y"/> and <see cref="Height"/> property values of this <see cref="Rectangle"/>.
        /// </summary>
        public float Bottom => Y + Height;

        /// <summary>
        /// Gets the x-coordinate of the left edge of this <see cref="Rectangle"/>.
        /// </summary>
        public float Left => X;

        /// <summary>
        /// Gets the x-coordinate that is the sum of <see cref="X"/> and <see cref="Width"/> property values of this <see cref="Rectangle"/>.
        /// </summary>
        public float Right => X + Width;

        /// <summary>
        /// Gets the upper left point of this <see cref="Rectangle"/>.
        /// </summary>
        public PointF TopLeft => new(X, Y);

        /// <summary>
        /// Gets the lower right point of this <see cref="Rectangle"/>.
        /// </summary>
        public PointF BottomRight => new(X + Width, Y + Height);

        /// <summary>
        /// Gets the coordinates of the upper-left corner of this <see cref="Rectangle"/>.
        /// </summary>
        public PointF Point => new(X, Y);

        /// <summary>
        /// Gets the size of this <see cref="Rectangle"/>.
        /// </summary>
        public SizeF Size => new(Width, Height);

        /// <summary>
        /// Adds the specified <see cref="PointF"/> to the specified <see cref="RectangleF"/>.
        /// </summary>
        /// <param name="rect">The <see cref="RectangleF"/> to add.</param>
        /// <param name="point">The <see cref="PointF"/> to add.</param>
        /// <returns>The <see cref="RectangleF"/> that is the result of the addition operation.</returns>
        public static RectangleF operator +(RectangleF rect, PointF point)
        {
            return new(rect.X + point.X, rect.Y + point.Y, rect.Width, rect.Height);
        }

        /// <summary>
        /// Adds the specified <see cref="SizeF"/> to the specified <see cref="RectangleF"/>.
        /// </summary>
        /// <param name="rect">The <see cref="RectangleF"/> to add.</param>
        /// <param name="size">The <see cref="SizeF"/> to add.</param>
        /// <returns>The <see cref="RectangleF"/> that is the result of the addition operation.</returns>
        public static RectangleF operator +(RectangleF rect, SizeF size)
        {
            return new(rect.X, rect.Y, rect.Width + size.Width, rect.Height + size.Height);
        }

        /// <summary>
        /// Returns the result of subtracting specified <see cref="PointF"/> from the specified <see cref="RectangleF"/>.
        /// </summary>
        /// <param name="rect">The <see cref="RectangleF"/> to be subtracted from.</param>
        /// <param name="point">The <see cref="Drawing.PointF"/> to subtract from the <see cref="RectangleF"/>.</param>
        /// <returns>The <see cref="RectangleF"/> that is the result of the subtraction operation.</returns>
        public static RectangleF operator -(RectangleF rect, PointF point)
        {
            return new(rect.X - point.X, rect.Y - point.Y, rect.Width, rect.Height);
        }

        /// <summary>
        /// Returns the result of subtracting specified <see cref="SizeF"/> from the specified <see cref="RectangleF"/>.
        /// </summary>
        /// <param name="rect">The <see cref="RectangleF"/> to be subtracted from.</param>
        /// <param name="size">The <see cref="SizeF"/> to subtract from the <see cref="RectangleF"/>.</param>
        /// <returns>The <see cref="RectangleF"/> that is the result of the subtraction operation.</returns>
        public static RectangleF operator -(RectangleF rect, SizeF size)
        {
            return new(rect.X, rect.Y, rect.Width - size.Width, rect.Height - size.Height);
        }

        /// <summary>
        /// Compares two <see cref="RectangleF"/>. The result specifies whether the values of the <see cref="Point"/> and <see cref="Size"/> properties of the two <see cref="RectangleF"/> are equal.
        /// </summary>
        /// <param name="left">A <see cref="RectangleF"/> to compare.</param>
        /// <param name="right">A <see cref="RectangleF"/> to compare.</param>
        /// <returns>true if the <see cref="Point"/> and <see cref="Size"/> values of the left and right <see cref="RectangleF"/> structures are equal; otherwise, false.</returns>
        public static bool operator ==(RectangleF left, RectangleF right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether the coordinates of the specified points are not equal.
        /// </summary>
        /// <param name="left">A <see cref="RectangleF"/> to compare.</param>
        /// <param name="right">A <see cref="RectangleF"/> to compare.</param>
        /// <returns>true if the <see cref="Point"/> and <see cref="Size"/> values of the left and right <see cref="RectangleF"/> differ; otherwise, false.</returns>
        public static bool operator !=(RectangleF left, RectangleF right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Creates a <see cref="RectangleF"/> with the specified edge locations.
        /// </summary>
        /// <param name="left">The x-coordinate of the upper-left corner of this <see cref="RectangleF"/>.</param>
        /// <param name="top">The y-coordinate of the upper-left corner of this <see cref="RectangleF"/>.</param>
        /// <param name="right">The x-coordinate of the lower-right corner of this <see cref="RectangleF"/>.</param>
        /// <param name="bottom">The y-coordinate of the lower-right corner of this <see cref="RectangleF"/>.</param>
        /// <returns>The new <see cref="RectangleF"/> that this method creates.</returns>
        public static RectangleF FromLTRB(float left, float top, float right, float bottom)
        {
            var r = new RectangleF(
                x: left,
                y: top,
                width: right - left,
                height: bottom - top);

            if (r.Width < 0) throw new ArgumentException(string.Format(Strings.LessThan, nameof(left), nameof(right)));

            if (r.Height < 0) throw new ArgumentException(string.Format(Strings.LessThan, nameof(top), nameof(bottom)));

            return r;
        }

        /// <summary>
        /// Creates and returns an enlarged copy of the specified <see cref="RectangleF"/>. The copy is enlarged by the specified amount. The original <see cref="RectangleF"/> remains unmodified.
        /// </summary>
        /// <param name="rect">The <see cref="RectangleF"/> with which to start. This rectangle is not modified.</param>
        /// <param name="x">The amount to inflate this <see cref="RectangleF"/> horizontally.</param>
        /// <param name="y">The amount to inflate this <see cref="RectangleF"/> vertically.</param>
        /// <returns>The enlarged <see cref="RectangleF"/>.</returns>
        public static RectangleF Inflate(RectangleF rect, float x, float y)
        {
            return new(
                rect.X - x,
                rect.Y - y,
                rect.Width + (2 * x),
                rect.Height + (2 * y));
        }

        /// <summary>
        /// Returns a third <see cref="RectangleF"/> that represents the intersection of two other <see cref="RectangleF"/>. If there is no intersection, an empty <see cref="RectangleF"/> is returned.
        /// </summary>
        /// <param name="a"> A rectangle to intersect.</param>
        /// <param name="b"> A rectangle to intersect.</param>
        /// <returns>A <see cref="RectangleF"/> that represents the intersection of <paramref name="a"/> and <paramref name="b"/>.</returns>
        public static RectangleF Intersect(RectangleF a, RectangleF b)
        {
            var x1 = MathF.Max(a.X, b.X);
            var x2 = MathF.Min(a.X + a.Width, b.X + b.Width);
            var y1 = Math.Max(a.Y, b.Y);
            var y2 = MathF.Min(a.Y + a.Height, b.Y + b.Height);

            if (x2 >= x1 && y2 >= y1)
            {
                return new RectangleF(x1, y1, x2 - x1, y2 - y1);
            }

            return Empty;
        }

        /// <summary>
        /// Gets a <see cref="RectangleF"/> that contains the union of two <see cref="RectangleF"/>.
        /// </summary>
        /// <param name="a">A rectangle to union.</param>
        /// <param name="b">A rectangle to union.</param>
        /// <returns>A <see cref="RectangleF"/> that bounds the union of the two <see cref="RectangleF"/>.</returns>
        public static RectangleF Union(RectangleF a, RectangleF b)
        {
            var x1 = MathF.Min(a.X, b.X);
            var x2 = MathF.Max(a.X + a.Width, b.X + b.Width);
            var y1 = MathF.Min(a.Y, b.Y);
            var y2 = MathF.Max(a.Y + a.Height, b.Y + b.Height);

            return new RectangleF(x1, y1, x2 - x1, y2 - y1);
        }

        /// <summary>
        /// Centers another rectangle in this rectangle.
        /// </summary>
        /// <param name="rect">The rectangle to center.</param>
        /// <returns>The centered rectangle.</returns>
        public RectangleF CenterRect(RectangleF rect)
        {
            return new RectangleF(
                X + ((Width - rect.Width) / 2),
                Y + ((Height - rect.Height) / 2),
                rect.Width,
                rect.Height);
        }

        /// <inheritdoc/>
        public bool Equals(RectangleF other)
        {
            return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is RectangleF other && Equals(other);
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