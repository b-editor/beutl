// Color.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Globalization;
using System.Runtime.Serialization;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.Resources;

namespace BEditor.Drawing
{
    /// <summary>
    /// Represents the ARGB (alpha, red, green, blue) color.
    /// </summary>
    [Serializable]
    public struct Color : IEquatable<Color>, IFormattable, ISerializable
    {
        private const int ARGBAlphaShift = 24;
        private const int ARGBRedShift = 16;
        private const int ARGBGreenShift = 8;
        private const int ARGBBlueShift = 0;

        private Color(byte a, byte r, byte g, byte b)
        {
            A = a;
            R = r;
            G = g;
            B = b;
        }

        private Color(SerializationInfo info, StreamingContext context)
        {
            A = info.GetByte(nameof(A));
            R = info.GetByte(nameof(R));
            G = info.GetByte(nameof(G));
            B = info.GetByte(nameof(B));
        }

        /// <summary>
        /// Gets or sets the alpha component value of this <see cref="Color"/>.
        /// </summary>
        public byte A { readonly get; set; }

        /// <summary>
        /// Gets or sets the red component value of this <see cref="Color"/>.
        /// </summary>
        public byte R { readonly get; set; }

        /// <summary>
        /// Gets or sets the green component value of this <see cref="Color"/>.
        /// </summary>
        public byte G { readonly get; set; }

        /// <summary>
        /// Gets or sets the blue component value of this <see cref="Color"/>.
        /// </summary>
        public byte B { readonly get; set; }

        /// <summary>
        /// Converts the <see cref="Color"/> to a <see cref="BGRA32"/>.
        /// </summary>
        /// <param name="value">A color.</param>
        public static implicit operator BGRA32(Color value)
        {
            return new(value.R, value.G, value.B, value.A);
        }

        /// <summary>
        /// Converts the <see cref="Color"/> to a <see cref="RGBA32"/>.
        /// </summary>
        /// <param name="value">A color.</param>
        public static implicit operator RGBA32(Color value)
        {
            return new(value.R, value.G, value.B, value.A);
        }

        /// <summary>
        /// Converts the <see cref="Color"/> to a <see cref="BGR24"/>.
        /// </summary>
        /// <param name="value">A color.</param>
        public static implicit operator BGR24(Color value)
        {
            return new(value.R, value.G, value.B);
        }

        /// <summary>
        /// Converts the <see cref="Color"/> to a <see cref="RGB24"/>.
        /// </summary>
        /// <param name="value">A color.</param>
        public static implicit operator RGB24(Color value)
        {
            return new(value.R, value.G, value.B);
        }

        /// <summary>
        /// Converts the <see cref="BGRA32"/> to a <see cref="Color"/>.
        /// </summary>
        /// <param name="value">A color.</param>
        public static implicit operator Color(BGRA32 value)
        {
            return new(value.A, value.R, value.G, value.B);
        }

        /// <summary>
        /// Converts the <see cref="RGBA32"/> to a <see cref="Color"/>.
        /// </summary>
        /// <param name="value">A color.</param>
        public static implicit operator Color(RGBA32 value)
        {
            return new(value.A, value.R, value.G, value.B);
        }

        /// <summary>
        /// Converts the <see cref="BGR24"/> to a <see cref="Color"/>.
        /// </summary>
        /// <param name="value">A color.</param>
        public static implicit operator Color(BGR24 value)
        {
            return new(255, value.R, value.G, value.B);
        }

        /// <summary>
        /// Converts the <see cref="RGB24"/> to a <see cref="Color"/>.
        /// </summary>
        /// <param name="value">A color.</param>
        public static implicit operator Color(RGB24 value)
        {
            return new(255, value.R, value.G, value.B);
        }

        /// <summary>
        /// Indicates whether two <see cref="Color"/> instances are equal.
        /// </summary>
        /// <param name="left">The first color to compare.</param>
        /// <param name="right">The second color to compare.</param>
        /// <returns>true if the values of <paramref name="left"/> and <paramref name="right"/> are equal; otherwise, false.</returns>
        public static bool operator ==(Color left, Color right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Indicates whether two <see cref="Color"/> instances are not equal.
        /// </summary>
        /// <param name="left">The first color to compare.</param>
        /// <param name="right">The second color to compare.</param>
        /// <returns>true if the values of <paramref name="left"/> and <paramref name="right"/> are not equal; otherwise, false.</returns>
        public static bool operator !=(Color left, Color right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Creates a <see cref="Color"/> structure from a 32-bit ARGB value.
        /// </summary>
        /// <param name="argb">A value specifying the 32-bit ARGB value.</param>
        /// <returns>The <see cref="Color"/> structure that this method creates.</returns>
        [Obsolete("Use FromInt32.")]
        public static Color FromARGB(int argb)
        {
            return FromARGB(unchecked((uint)argb));
        }

        /// <summary>
        /// Creates a <see cref="Color"/> structure from a 32-bit ARGB value.
        /// </summary>
        /// <param name="argb">A value specifying the 32-bit ARGB value.</param>
        /// <returns>The <see cref="Color"/> structure that this method creates.</returns>
        [Obsolete("Use FromUInt32.")]
        public static Color FromARGB(uint argb)
        {
            long color = argb;
            return new(
                unchecked((byte)(color >> ARGBAlphaShift)),
                unchecked((byte)(color >> ARGBRedShift)),
                unchecked((byte)(color >> ARGBGreenShift)),
                unchecked((byte)(color >> ARGBBlueShift)));
        }

        /// <summary>
        /// Creates a <see cref="Color"/> structure from the four ARGB component.
        /// </summary>
        /// <param name="a">The alpha component.</param>
        /// <param name="r">The red component.</param>
        /// <param name="g">The green component.</param>
        /// <param name="b">The blue component.</param>
        /// <returns>The <see cref="Color"/> that this method creates.</returns>
        [Obsolete("Use FromArgb.")]
        public static Color FromARGB(byte a, byte r, byte g, byte b)
        {
            return new(a, r, g, b);
        }

        /// <summary>
        /// Creates a <see cref="Color"/> structure from the html color.
        /// </summary>
        /// <param name="htmlcolor">The value that specifies the html color.</param>
        /// <returns>The <see cref="Color"/> that this method creates.</returns>
        [Obsolete("Use Parse.")]
        public static Color FromHTML(string? htmlcolor)
        {
            return Parse(htmlcolor);
        }

        /// <summary>
        /// Creates a <see cref="Color"/> structure from a 32-bit ARGB value.
        /// </summary>
        /// <param name="argb">A value specifying the 32-bit ARGB value.</param>
        /// <returns>The <see cref="Color"/> structure that this method creates.</returns>
        public static Color FromInt32(int argb)
        {
            return FromUInt32(unchecked((uint)argb));
        }

        /// <summary>
        /// Creates a <see cref="Color"/> structure from a 32-bit ARGB value.
        /// </summary>
        /// <param name="argb">A value specifying the 32-bit ARGB value.</param>
        /// <returns>The <see cref="Color"/> structure that this method creates.</returns>
        public static Color FromUInt32(uint argb)
        {
            long color = argb;
            return new(
                unchecked((byte)(color >> ARGBAlphaShift)),
                unchecked((byte)(color >> ARGBRedShift)),
                unchecked((byte)(color >> ARGBGreenShift)),
                unchecked((byte)(color >> ARGBBlueShift)));
        }

        /// <summary>
        /// Creates a <see cref="Color"/> structure from the four ARGB component.
        /// </summary>
        /// <param name="a">The alpha component.</param>
        /// <param name="r">The red component.</param>
        /// <param name="g">The green component.</param>
        /// <param name="b">The blue component.</param>
        /// <returns>The <see cref="Color"/> that this method creates.</returns>
        public static Color FromArgb(byte a, byte r, byte g, byte b)
        {
            return new(a, r, g, b);
        }

        /// <summary>
        /// Creates a <see cref="Color"/> structure from the html color.
        /// </summary>
        /// <param name="s">The value that specifies the html color.</param>
        /// <returns>The <see cref="Color"/> that this method creates.</returns>
        public static Color Parse(string? s)
        {
            if (string.IsNullOrWhiteSpace(s) || s is "#")
            {
                return MaterialColors.Dark;
            }

            s = "0x" + s.Trim('#');

            var argb = Convert.ToUInt32(s, 16);

            return FromUInt32(argb);
        }

        /// <inheritdoc/>
        public readonly override bool Equals(object? obj)
        {
            return obj is Color color && Equals(color);
        }

        /// <inheritdoc/>
        public readonly bool Equals(Color other)
        {
            return R == other.R && G == other.G && B == other.B && A == other.A;
        }

        /// <inheritdoc/>
        public readonly override int GetHashCode()
        {
            return HashCode.Combine(R, G, B, A);
        }

        /// <inheritdoc cref="IFormattable.ToString(string?, IFormatProvider?)"/>
        public readonly string ToString(string? format)
        {
            return ToString(format, CultureInfo.CurrentCulture);
        }

        /// <inheritdoc/>
        public readonly string ToString(string? format, IFormatProvider? formatProvider)
        {
            static string Throw(string format)
            {
                throw new FormatException(string.Format(Strings.FormetException, format));
            }

            if (string.IsNullOrEmpty(format))
            {
                format = "#argb";
            }

            format = format.ToUpperInvariant();

            var colorformat = format
                .Trim('#')
                .Replace("0X", string.Empty)
                .Replace("-L", string.Empty)
                .Replace("-U", string.Empty);

            var islower = format.Contains("-L");
            var result = string.Empty;

            foreach (var c in colorformat)
            {
                result += c switch
                {
                    'R' => Tohex(R),
                    'G' => Tohex(G),
                    'B' => Tohex(B),
                    'A' => Tohex(A),
                    _ => Throw(format),
                };
            }

            if (format.Contains("#"))
            {
                result = "#" + result;
            }
            else if (format.Contains("0X"))
            {
                result = "0x" + result;
            }

            if (islower)
            {
                result = result.ToLowerInvariant();
            }

            return result;
        }

        /// <inheritdoc/>
        public readonly void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(A), A);
            info.AddValue(nameof(R), R);
            info.AddValue(nameof(G), G);
            info.AddValue(nameof(B), B);
        }

        /// <summary>
        /// Converts this 32Bit color to HSV.
        /// </summary>
        /// <returns>Returns the HSV.</returns>
        public readonly Hsv ToHsv()
        {
            var max = Math.Max(Math.Max(R, G), B);
            var min = Math.Min(Math.Min(R, G), B);
            var hsv = new Hsv(0, 0, max);

            if (max != min)
            {
                // H(色相)
                if (max == R) hsv.H = 60 * (G - B) / (max - min);
                if (max == G) hsv.H = (60 * (B - R) / (max - min)) + 120;
                if (max == B) hsv.H = (60 * (R - G) / (max - min)) + 240;

                // S(彩度)
                hsv.S = (max - min) / max;
            }

            if (hsv.H < 0)
            {
                hsv.H += 360;
            }

            hsv.H = Math.Round(hsv.H);
            hsv.S = Math.Round(hsv.S * 100);
            hsv.V = Math.Round(hsv.V / 255 * 100);
            return hsv;
        }

        /// <summary>
        /// Converts this 32Bit color to CMYK.
        /// </summary>
        /// <returns>Returns the CMYK.</returns>
        public readonly Cmyk ToCmyk()
        {
            var r = (double)R;
            var g = (double)G;
            var b = (double)B;

            var rr = r / 255.0;
            var gg = g / 255.0;
            var bb = b / 255.0;

            var k = 1.0 - Math.Max(Math.Max(rr, gg), bb);
            var c = (1.0 - rr - k) / (1.0 - k);
            var m = (1.0 - gg - k) / (1.0 - k);
            var y = (1.0 - bb - k) / (1.0 - k);

            c *= 100.0;
            m *= 100.0;
            y *= 100.0;
            k *= 100.0;

            return new Cmyk(c, m, y, k);
        }

        private static string Tohex(byte value)
        {
            return value.ToString("X2");
        }
    }
}