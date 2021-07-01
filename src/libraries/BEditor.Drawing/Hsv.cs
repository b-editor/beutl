// Hsv.cs
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
    /// Represents the HSV (Hue, Saturation, Brightness) color.
    /// </summary>
    [Serializable]
    public struct Hsv : ISerializable, IEquatable<Hsv>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Hsv"/> struct.
        /// </summary>
        /// <param name="h">The hue.</param>
        /// <param name="s">The saturation.</param>
        /// <param name="v">The brightness.</param>
        public Hsv(double h, double s, double v)
        {
            H = h;
            S = s;
            V = v;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Hsv"/> struct.
        /// </summary>
        /// <param name="rgb">The rgb color.</param>
        public Hsv(Color rgb)
        {
            this = rgb.ToHsv();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Hsv"/> struct.
        /// </summary>
        /// <param name="cmyk">The CMYK.</param>
        public Hsv(Cmyk cmyk)
        {
            this = cmyk.ToHsv();
        }

        private Hsv(SerializationInfo info, StreamingContext context)
        {
            H = info.GetDouble(nameof(H));
            S = info.GetDouble(nameof(S));
            V = info.GetDouble(nameof(V));
        }

        /// <summary>
        /// Gets or sets the hue component value of this <see cref="Hsv"/>.
        /// </summary>
        public double H { readonly get; set; }

        /// <summary>
        /// Gets or sets the saturation component value of this <see cref="Hsv"/>.
        /// </summary>
        public double S { readonly get; set; }

        /// <summary>
        /// Gets or sets the brightness component value of this <see cref="Hsv"/>.
        /// </summary>
        public double V { readonly get; set; }

        /// <summary>
        /// Indicates whether two <see cref="Hsv"/> instances are equal.
        /// </summary>
        /// <param name="left">The first color to compare.</param>
        /// <param name="right">The second color to compare.</param>
        /// <returns>true if the values of <paramref name="left"/> and <paramref name="right"/> are equal; otherwise, false.</returns>
        public static bool operator ==(Hsv left, Hsv right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Indicates whether two <see cref="Hsv"/> instances are not equal.
        /// </summary>
        /// <param name="left">The first color to compare.</param>
        /// <param name="right">The second color to compare.</param>
        /// <returns>true if the values of <paramref name="left"/> and <paramref name="right"/> are not equal; otherwise, false.</returns>
        public static bool operator !=(Hsv left, Hsv right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Converts this HSV to RGB.
        /// </summary>
        /// <returns>Returns the RGB.</returns>
        public readonly Color ToColor()
        {
            double r;
            double g;
            double b;

            if (S == 0)
            {
                r = g = b = Math.Round(V * 2.55);
                return Color.FromARGB(255, (byte)r, (byte)g, (byte)b);
            }

            var hh = H;
            var ss = S / 100.0;
            var vv = V / 100.0;
            if (hh >= 360.0)
                hh = 0.0;
            hh /= 60.0;

            var i = (long)hh;
            var ff = hh - i;
            var p = vv * (1.0 - ss);
            var q = vv * (1.0 - (ss * ff));
            var t = vv * (1.0 - (ss * (1.0 - ff)));

            switch ((int)i)
            {
                case 0:
                    r = vv;
                    g = t;
                    b = p;
                    break;
                case 1:
                    r = q;
                    g = vv;
                    b = p;
                    break;
                case 2:
                    r = p;
                    g = vv;
                    b = t;
                    break;
                case 3:
                    r = p;
                    g = q;
                    b = vv;
                    break;
                case 4:
                    r = t;
                    g = p;
                    b = vv;
                    break;
                default:
                    r = vv;
                    g = p;
                    b = q;
                    break;
            }

            r = Math.Round(r * 255.0);
            g = Math.Round(g * 255.0);
            b = Math.Round(b * 255.0);

            return Color.FromARGB(255, (byte)r, (byte)g, (byte)b);
        }

        /// <summary>
        /// Converts this HSV to CMYK.
        /// </summary>
        /// <returns>Returns the CMYK.</returns>
        public readonly Cmyk ToCmyk()
        {
            return ToColor().ToCmyk();
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return obj is Hsv hsv && Equals(hsv);
        }

        /// <inheritdoc/>
        public bool Equals(Hsv other)
        {
            return H == other.H &&
                   S == other.S &&
                   V == other.V;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(H, S, V);
        }

        /// <inheritdoc/>
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(H), H);
            info.AddValue(nameof(S), S);
            info.AddValue(nameof(V), V);
        }
    }
}