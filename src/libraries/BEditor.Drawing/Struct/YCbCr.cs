// YCbCr.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace BEditor.Drawing
{
    /// <summary>
    /// Represents the YCbCr (luminance, blue chroma, red chroma) color.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct YCbCr : IEquatable<YCbCr>, ISerializable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="YCbCr"/> struct.
        /// </summary>
        /// <param name="y">The y luminance component.</param>
        /// <param name="cb">The cb chroma component.</param>
        /// <param name="cr">The cr chroma component.</param>
        public YCbCr(float y, float cb, float cr)
        {
            Y = y;
            Cb = cb;
            Cr = cr;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="YCbCr"/> struct.
        /// </summary>
        /// <param name="rgb">The rgb color.</param>
        public YCbCr(Color rgb)
        {
            this = rgb.ToYCbCr();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="YCbCr"/> struct.
        /// </summary>
        /// <param name="hsv">The HSV.</param>
        public YCbCr(Hsv hsv)
        {
            this = hsv.ToYCbCr();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="YCbCr"/> struct.
        /// </summary>
        /// <param name="cmyk">The CMYK.</param>
        public YCbCr(Cmyk cmyk)
        {
            this = cmyk.ToYCbCr();
        }

        private YCbCr(SerializationInfo info, StreamingContext context)
        {
            Y = info.GetSingle(nameof(Y));
            Cb = info.GetSingle(nameof(Cb));
            Cr = info.GetSingle(nameof(Cr));
        }

        /// <summary>
        /// Gets or sets the Y luminance component.
        /// <remarks>A value ranging between 0 and 255.</remarks>
        /// </summary>
        public float Y { readonly get; set; }

        /// <summary>
        /// Gets or sets the Cb chroma component.
        /// <remarks>A value ranging between 0 and 255.</remarks>
        /// </summary>
        public float Cb { readonly get; set; }

        /// <summary>
        /// Gets or sets the Cr chroma component.
        /// <remarks>A value ranging between 0 and 255.</remarks>
        /// </summary>
        public float Cr { readonly get; set; }

        /// <summary>
        /// Indicates whether two <see cref="Cmyk"/> instances are equal.
        /// </summary>
        /// <param name="left">The first color to compare.</param>
        /// <param name="right">The second color to compare.</param>
        /// <returns>true if the values of <paramref name="left"/> and <paramref name="right"/> are equal; otherwise, false.</returns>
        public static bool operator ==(YCbCr left, YCbCr right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Indicates whether two <see cref="Cmyk"/> instances are not equal.
        /// </summary>
        /// <param name="left">The first color to compare.</param>
        /// <param name="right">The second color to compare.</param>
        /// <returns>true if the values of <paramref name="left"/> and <paramref name="right"/> are not equal; otherwise, false.</returns>
        public static bool operator !=(YCbCr left, YCbCr right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Converts this YCbCr to RGB.
        /// </summary>
        /// <returns>Returns the RGB.</returns>
        public readonly Color ToColor()
        {
            var y = Y;
            var cb = Cb - 128F;
            var cr = Cr - 128F;

            var r = MathF.Round(y + (1.402F * cr), MidpointRounding.AwayFromZero);
            var g = MathF.Round(y - (0.344136F * cb) - (0.714136F * cr), MidpointRounding.AwayFromZero);
            var b = MathF.Round(y + (1.772F * cb), MidpointRounding.AwayFromZero);

            return Color.FromArgb(255, (byte)(r / 255), (byte)(g / 255), (byte)(b / 255));
        }

        /// <summary>
        /// Converts this YCbCr to CMYK.
        /// </summary>
        /// <returns>Returns the CMYK.</returns>
        public readonly Cmyk ToCmyk()
        {
            return ToColor().ToCmyk();
        }

        /// <summary>
        /// Converts this YCbCr to HSV.
        /// </summary>
        /// <returns>Returns the HSV.</returns>
        public readonly Hsv ToHsv()
        {
            return ToColor().ToHsv();
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is YCbCr cr && Equals(cr);
        }

        /// <inheritdoc/>
        public bool Equals(YCbCr other)
        {
            return Y == other.Y &&
                   Cb == other.Cb &&
                   Cr == other.Cr;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Y, Cb, Cr);
        }

        /// <inheritdoc/>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(Y), Y);
            info.AddValue(nameof(Cb), Cb);
            info.AddValue(nameof(Cr), Cr);
        }
    }
}