// SizeF.cs
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
    public readonly struct SizeF : IEquatable<SizeF>, ISerializable
    {
        /// <summary>
        /// Gets a <see cref="SizeF"/> that has a <see cref="Width"/> and <see cref="Width"/> value of 0.
        /// </summary>
        public static readonly SizeF Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="SizeF"/> struct.
        /// </summary>
        /// <param name="width">The width of the <see cref="SizeF"/>.</param>
        /// <param name="height">The height of the <see cref="SizeF"/>.</param>
        public SizeF(float width, float height)
        {
            if (width < 0) throw new ArgumentOutOfRangeException(nameof(width), string.Format(Strings.LessThan, nameof(width), 0));
            if (height < 0) throw new ArgumentOutOfRangeException(nameof(height), string.Format(Strings.LessThan, nameof(height), 0));

            Width = width;
            Height = height;
        }

        private SizeF(SerializationInfo info, StreamingContext context)
        {
            Width = info.GetSingle(nameof(Width));
            Height = info.GetSingle(nameof(Height));
        }

        /// <summary>
        /// Gets the width of this <see cref="Size"/>.
        /// </summary>
        public float Width { get; }

        /// <summary>
        /// Gets the height of this <see cref="Size"/>.
        /// </summary>
        public float Height { get; }

        /// <summary>
        /// Gets the aspect ratio of this <see cref="Size"/>.
        /// </summary>
        public float Aspect => Width / Height;

        /// <summary>
        /// Compares two <see cref="SizeF"/>. The result specifies whether the values of the <see cref="Width"/> and <see cref="Height"/> properties of the two <see cref="SizeF"/> are equal.
        /// </summary>
        /// <param name="left">A <see cref="SizeF"/> to compare.</param>
        /// <param name="right">A <see cref="SizeF"/> to compare.</param>
        /// <returns>true if the <see cref="Width"/> and <see cref="Height"/> values of the left and right <see cref="SizeF"/> are equal; otherwise, false.</returns>
        public static bool operator ==(SizeF left, SizeF right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Determines whether the coordinates of the specified points are not equal.
        /// </summary>
        /// <param name="left">A <see cref="SizeF"/> to compare.</param>
        /// <param name="right">A <see cref="SizeF"/> to compare.</param>
        /// <returns>true if the <see cref="Width"/> and <see cref="Height"/> values of the left and right <see cref="SizeF"/> structures differ; otherwise, false.</returns>
        public static bool operator !=(SizeF left, SizeF right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Adds the width and height of one <see cref="SizeF"/> to the width and height of another <see cref="SizeF"/>.
        /// </summary>
        /// <param name="size1">The first <see cref="SizeF"/> to add.</param>
        /// <param name="size2">The second <see cref="SizeF"/> to add.</param>
        /// <returns>A <see cref="SizeF"/> that in the result of the addition operation.</returns>
        public static SizeF operator +(SizeF size1, SizeF size2)
        {
            return Add(size1, size2);
        }

        /// <summary>
        /// Subtracts the width and height of one <see cref="SizeF"/> from the width and height of another <see cref="SizeF"/>.
        /// </summary>
        /// <param name="size1">The <see cref="SizeF"/> on the left side of the subtraction operator.</param>
        /// <param name="size2">The <see cref="SizeF"/> on the right side of the subtraction operator.</param>
        /// <returns>A <see cref="SizeF"/> that is a result of the subtraction operation.</returns>
        public static SizeF operator -(SizeF size1, SizeF size2)
        {
            return Subtract(size1, size2);
        }

        /// <summary>
        /// Multiplies the specified <see cref="SizeF"/> by the specified integer.
        /// </summary>
        /// <param name="left">The multiplicand.</param>
        /// <param name="right">The multiplier.</param>
        /// <returns>The result of multiplying left's width and height by right.</returns>
        public static SizeF operator *(SizeF left, float right)
        {
            return new(left.Width * right, left.Height * right);
        }

        /// <summary>
        /// Divides the specified <see cref="SizeF"/> by the specified integer.
        /// </summary>
        /// <param name="left">The dividend.</param>
        /// <param name="right">The divisor.</param>
        /// <returns>A new <see cref="SizeF"/>, which contains the result of dividing left's height by right and left's width by right, respectively.</returns>
        public static SizeF operator /(SizeF left, int right)
        {
            return new(left.Width / right, left.Height / right);
        }

        /// <summary>
        /// Adds the width and height of one <see cref="SizeF"/> to the width and height of another <see cref="SizeF"/>.
        /// </summary>
        /// <param name="size1">The first <see cref="SizeF"/> to add.</param>
        /// <param name="size2">The second <see cref="SizeF"/> to add.</param>
        /// <returns>A <see cref="SizeF"/> that in the result of the addition operation.</returns>
        public static SizeF Add(SizeF size1, SizeF size2)
        {
            return new(size1.Width + size2.Width, size1.Height + size2.Height);
        }

        /// <summary>
        /// Subtracts the width and height of one <see cref="SizeF"/> from the width and height of another <see cref="SizeF"/>.
        /// </summary>
        /// <param name="size1">The <see cref="SizeF"/> on the left side of the subtraction operator.</param>
        /// <param name="size2">The <see cref="SizeF"/> on the right side of the subtraction operator.</param>
        /// <returns>A <see cref="SizeF"/> that is a result of the subtraction operation.</returns>
        public static SizeF Subtract(SizeF size1, SizeF size2)
        {
            return new(size1.Width - size2.Width, size1.Height - size2.Height);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is SizeF size && Equals(size);
        }

        /// <inheritdoc/>
        public bool Equals(SizeF other)
        {
            return Width == other.Width && Height == other.Height;
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