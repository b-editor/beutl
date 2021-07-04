// Cmyk.cs
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
    /// Represents the CMYK (Cyan, Magenta, Yellow, Key plate) color.
    /// </summary>
    [Serializable]
    public struct Cmyk : IEquatable<Cmyk>, ISerializable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Cmyk"/> struct.
        /// </summary>
        /// <param name="c">The cyan.</param>
        /// <param name="m">The magenta.</param>
        /// <param name="y">The yellow.</param>
        /// <param name="k">The key plate.</param>
        public Cmyk(double c, double m, double y, double k)
        {
            C = c;
            M = m;
            Y = y;
            K = k;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Cmyk"/> struct.
        /// </summary>
        /// <param name="rgb">The RGB color.</param>
        public Cmyk(Color rgb)
        {
            this = rgb.ToCmyk();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Cmyk"/> struct.
        /// </summary>
        /// <param name="hsv">The HSV.</param>
        public Cmyk(Hsv hsv)
        {
            this = hsv.ToCmyk();
        }

        private Cmyk(SerializationInfo info, StreamingContext context)
        {
            C = info.GetDouble(nameof(C));
            M = info.GetDouble(nameof(M));
            Y = info.GetDouble(nameof(Y));
            K = info.GetDouble(nameof(K));
        }

        /// <summary>
        /// Gets or sets the cyan component value of this <see cref="Cmyk"/>.
        /// </summary>
        public double C { readonly get; set; }

        /// <summary>
        /// Gets or sets the magenta component value of this <see cref="Cmyk"/>.
        /// </summary>
        public double M { readonly get; set; }

        /// <summary>
        /// Gets or sets the yellow component value of this <see cref="Cmyk"/>.
        /// </summary>
        public double Y { readonly get; set; }

        /// <summary>
        /// Gets or sets the key plate component value of this <see cref="Cmyk"/>.
        /// </summary>
        public double K { readonly get; set; }

        /// <summary>
        /// Indicates whether two <see cref="Cmyk"/> instances are equal.
        /// </summary>
        /// <param name="left">The first color to compare.</param>
        /// <param name="right">The second color to compare.</param>
        /// <returns>true if the values of <paramref name="left"/> and <paramref name="right"/> are equal; otherwise, false.</returns>
        public static bool operator ==(Cmyk left, Cmyk right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Indicates whether two <see cref="Cmyk"/> instances are not equal.
        /// </summary>
        /// <param name="left">The first color to compare.</param>
        /// <param name="right">The second color to compare.</param>
        /// <returns>true if the values of <paramref name="left"/> and <paramref name="right"/> are not equal; otherwise, false.</returns>
        public static bool operator !=(Cmyk left, Cmyk right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Converts this CMYK to RGB.
        /// </summary>
        /// <returns>Returns the RGB.</returns>
        public readonly Color ToColor()
        {
            var cc = C / 100.0;
            var mm = M / 100.0;
            var yy = Y / 100.0;
            var kk = K / 100.0;

            var r = (1.0 - cc) * (1.0 - kk);
            var g = (1.0 - mm) * (1.0 - kk);
            var b = (1.0 - yy) * (1.0 - kk);
            r = Math.Round(r * 255.0);
            g = Math.Round(g * 255.0);
            b = Math.Round(b * 255.0);

            return Color.FromArgb(255, (byte)r, (byte)g, (byte)b);
        }

        /// <summary>
        /// Converts this CMYK to HSV.
        /// </summary>
        /// <returns>Returns the HSV.</returns>
        public readonly Hsv ToHsv()
        {
            return ToColor().ToHsv();
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is Cmyk cmyk && Equals(cmyk);
        }

        /// <inheritdoc/>
        public bool Equals(Cmyk other)
        {
            return C == other.C &&
                   M == other.M &&
                   Y == other.Y &&
                   K == other.K;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(C, M, Y, K);
        }

        /// <inheritdoc/>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(C), C);
            info.AddValue(nameof(M), M);
            info.AddValue(nameof(Y), Y);
            info.AddValue(nameof(K), K);
        }
    }
}