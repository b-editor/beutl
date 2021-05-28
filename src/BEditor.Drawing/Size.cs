// Size.cs
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
    /// Stores an ordered pair of integers, which specify a <see cref="Height"/> and <see cref="Width"/>.
    /// </summary>
    [Serializable]
    public readonly struct Size : IEquatable<Size>, ISerializable
    {
        /// <summary>
        /// Gets a <see cref="Size"/> that has a <see cref="Width"/> and <see cref="Width"/> value of 0.
        /// </summary>
        public static readonly Size Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="Size"/> struct.
        /// </summary>
        /// <param name="width">The width of the <see cref="Size"/>.</param>
        /// <param name="height">The height of the <see cref="Size"/>.</param>
        public Size(int width, int height)
        {
            if (width < 0) throw new ArgumentOutOfRangeException(nameof(width), string.Format(Strings.LessThan, nameof(width), 0));
            if (height < 0) throw new ArgumentOutOfRangeException(nameof(height), string.Format(Strings.LessThan, nameof(height), 0));

            Width = width;
            Height = height;
        }

        private Size(SerializationInfo info, StreamingContext context)
        {
            Width = info.GetInt32(nameof(Width));
            Height = info.GetInt32(nameof(Height));
        }

        /// <summary>
        /// Gets the width of this <see cref="Size"/>.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the height of this <see cref="Size"/>.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets the aspect ratio of this <see cref="Size"/>.
        /// </summary>
        public float Aspect => Width / ((float)Height);

        /// <summary>
        /// Compares two <see cref="Size"/>. The result specifies whether the values of the <see cref="Width"/> and <see cref="Height"/> properties of the two <see cref="Size"/> are equal.
        /// </summary>
        /// <param name="left">A <see cref="Size"/> to compare.</param>
        /// <param name="right">A <see cref="Size"/> to compare.</param>
        /// <returns>true if the <see cref="Width"/> and <see cref="Height"/> values of the left and right <see cref="Size"/> are equal; otherwise, false.</returns>
        public static bool operator ==(Size left, Size right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether the coordinates of the specified points are not equal.
        /// </summary>
        /// <param name="left">A <see cref="Size"/> to compare.</param>
        /// <param name="right">A <see cref="Size"/> to compare.</param>
        /// <returns>true if the <see cref="Width"/> and <see cref="Height"/> values of the left and right <see cref="Size"/> structures differ; otherwise, false.</returns>
        public static bool operator !=(Size left, Size right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Adds the width and height of one <see cref="Size"/> to the width and height of another <see cref="Size"/>.
        /// </summary>
        /// <param name="size1">The first <see cref="Size"/> to add.</param>
        /// <param name="size2">The second <see cref="Size"/> to add.</param>
        /// <returns>A <see cref="Size"/> that in the result of the addition operation.</returns>
        public static Size operator +(Size size1, Size size2)
        {
            return Add(size1, size2);
        }

        /// <summary>
        /// Subtracts the width and height of one <see cref="Size"/> from the width and height of another <see cref="Size"/>.
        /// </summary>
        /// <param name="size1">The <see cref="Size"/> on the left side of the subtraction operator.</param>
        /// <param name="size2">The <see cref="Size"/> on the right side of the subtraction operator.</param>
        /// <returns>A <see cref="Size"/> that is a result of the subtraction operation.</returns>
        public static Size operator -(Size size1, Size size2)
        {
            return Subtract(size1, size2);
        }

        /// <summary>
        /// Multiplies the specified <see cref="Size"/> by the specified integer.
        /// </summary>
        /// <param name="left">The multiplicand.</param>
        /// <param name="right">The multiplier.</param>
        /// <returns>The result of multiplying left's width and height by right.</returns>
        public static Size operator *(Size left, int right)
        {
            return new(left.Width * right, left.Height * right);
        }

        /// <summary>
        /// Divides the specified <see cref="Size"/> by the specified integer.
        /// </summary>
        /// <param name="left">The dividend.</param>
        /// <param name="right">The divisor.</param>
        /// <returns>A new <see cref="Size"/>, which contains the result of dividing left's height by right and left's width by right, respectively.</returns>
        public static Size operator /(Size left, int right)
        {
            return new(left.Width / right, left.Height / right);
        }

        /// <summary>
        /// Adds the width and height of one <see cref="Size"/> to the width and height of another <see cref="Size"/>.
        /// </summary>
        /// <param name="size1">The first <see cref="Size"/> to add.</param>
        /// <param name="size2">The second <see cref="Size"/> to add.</param>
        /// <returns>A <see cref="Size"/> that in the result of the addition operation.</returns>
        public static Size Add(Size size1, Size size2)
        {
            return new(size1.Width + size2.Width, size1.Height + size2.Height);
        }

        /// <summary>
        /// Subtracts the width and height of one <see cref="Size"/> from the width and height of another <see cref="Size"/>.
        /// </summary>
        /// <param name="size1">The <see cref="Size"/> on the left side of the subtraction operator.</param>
        /// <param name="size2">The <see cref="Size"/> on the right side of the subtraction operator.</param>
        /// <returns>A <see cref="Size"/> that is a result of the subtraction operation.</returns>
        public static Size Subtract(Size size1, Size size2)
        {
            return new(size1.Width - size2.Width, size1.Height - size2.Height);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is Size size && Equals(size);
        }

        /// <inheritdoc/>
        public bool Equals(Size other)
        {
            return Width == other.Width && Height == other.Height && Aspect == other.Aspect;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Width, Height, Aspect);
        }

        /// <inheritdoc/>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(Width), Width);
            info.AddValue(nameof(Height), Height);
        }
    }
}