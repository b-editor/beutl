// Hsv.cs
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
    /// Represents the HSV (Hue, Saturation, Brightness) color.
    /// </summary>
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Hsv : ISerializable, IEquatable<Hsv>
    {
        // Hue 0 - 360
        // Saturation 0-100%
        // Value 0-100%

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
            var h = H;
            var s = S;
            var v = V;

            if (H == 360)
            {
                h = 0;
            }

            s /= 100;
            v /= 100;

            if (s == 0)
            {
                var result = Color.FromArgb(255, 0, 0, 0);
                result.R = (byte)(v * 255);
                result.G = (byte)(v * 255);
                result.B = (byte)(v * 255);
                return result;
            }

            var dh = Math.Floor(h / 60);
            var p = v * (1 - s);
            var q = v * (1 - (s * ((h / 60) - dh)));
            var t = v * (1 - (s * (1 - ((h / 60) - dh))));
            double r = 0;
            double g = 0;
            double b = 0;

            switch (dh)
            {
                case 0:
                    r = v;
                    g = t;
                    b = p;
                    break;
                case 1:
                    r = q;
                    g = v;
                    b = p;
                    break;
                case 2:
                    r = p;
                    g = v;
                    b = t;
                    break;
                case 3:
                    r = p;
                    g = q;
                    b = v;
                    break;
                case 4:
                    r = t;
                    g = p;
                    b = v;
                    break;
                case 5:
                    r = v;
                    g = p;
                    b = q;
                    break;
            }

            r = Math.Round(r * 255);
            g = Math.Round(g * 255);
            b = Math.Round(b * 255);
            return Color.FromArgb(255, (byte)r, (byte)g, (byte)b);
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