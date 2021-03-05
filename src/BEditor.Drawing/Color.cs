using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.Properties;

namespace BEditor.Drawing
{
    [Serializable]
    public struct Color : IEquatable<Color>, IFormattable, ISerializable
    {
        private const int ARGBAlphaShift = 24;
        private const int ARGBRedShift = 16;
        private const int ARGBGreenShift = 8;
        private const int ARGBBlueShift = 0;
        private readonly byte a;
        private readonly byte r;
        private readonly byte g;
        private readonly byte b;

        #region Colors

        public static readonly Color Light = FromARGB(255, 255, 255, 255);
        public static readonly Color Dark = FromARGB(255, 0, 0, 0);
        public static readonly Color Red = FromARGB(255, 244, 67, 54);
        public static readonly Color Pink = FromARGB(255, 233, 30, 99);
        public static readonly Color Purple = FromARGB(255, 156, 39, 176);
        public static readonly Color DeepPurple = FromARGB(255, 103, 58, 183);
        public static readonly Color Indigo = FromARGB(255, 63, 81, 181);
        public static readonly Color Blue = FromARGB(255, 33, 150, 243);
        public static readonly Color LightBlue = FromARGB(255, 3, 169, 244);
        public static readonly Color Cyan = FromARGB(255, 0, 188, 212);
        public static readonly Color Teal = FromARGB(255, 0, 150, 136);
        public static readonly Color Green = FromARGB(255, 76, 175, 80);
        public static readonly Color LightGreen = FromARGB(255, 139, 195, 74);
        public static readonly Color Lime = FromARGB(255, 205, 220, 57);
        public static readonly Color Yellow = FromARGB(255, 255, 235, 59);
        public static readonly Color Amber = FromARGB(255, 255, 193, 7);
        public static readonly Color Orange = FromARGB(255, 255, 152, 0);
        public static readonly Color DeepOrange = FromARGB(255, 255, 87, 34);
        public static readonly Color Brown = FromARGB(255, 121, 85, 72);
        public static readonly Color Grey = FromARGB(255, 158, 158, 158);
        public static readonly Color BlueGrey = FromARGB(255, 96, 125, 139);

        #endregion

        private Color(byte a, byte r, byte g, byte b)
        {
            this.a = a;
            this.r = r;
            this.g = g;
            this.b = b;
        }
        private Color(SerializationInfo info, StreamingContext context)
        {
            a = info.GetByte(nameof(A));
            r = info.GetByte(nameof(R));
            g = info.GetByte(nameof(G));
            b = info.GetByte(nameof(B));
        }

        public byte A
            => a;
        public byte R
            => r;
        public byte G
            => g;
        public byte B
            => b;

        public static Color FromARGB(int argb)
            => FromARGB(unchecked((uint)argb));
        public static Color FromARGB(uint argb)
        {
            long color = argb;
            return new(
                unchecked((byte)(color >> ARGBAlphaShift)),
                unchecked((byte)(color >> ARGBRedShift)),
                unchecked((byte)(color >> ARGBGreenShift)),
                unchecked((byte)(color >> ARGBBlueShift)));
        }
        public static Color FromARGB(byte a, byte r, byte g, byte b)
            => new(a, r, g, b);
        public static Color FromHTML(string? htmlcolor)
        {
            if (string.IsNullOrWhiteSpace(htmlcolor) || htmlcolor is "#") return Dark;

            htmlcolor = "0x" + htmlcolor.Replace("#", "");

            var argb = Convert.ToUInt32(htmlcolor, 16);

            return FromARGB(argb);
        }

        public override bool Equals(object? obj)
            => obj is Color color && Equals(color);
        public bool Equals(Color other)
            => R == other.R && G == other.G && B == other.B && A == other.A;
        public override int GetHashCode()
            => HashCode.Combine(R, G, B, A);
        public string ToString(string? format)
            => ToString(format, CultureInfo.CurrentCulture);
        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            static string Throw(string format)
                => throw new FormatException(string.Format(Resources.FormetException, format));

            if (string.IsNullOrEmpty(format)) format = "#argb";
            format = format.ToUpperInvariant();

            string colorformat = format
                .Replace("#", "")
                .Replace("0X", "")
                .Replace("-L", "")
                .Replace("-U", "");

            bool islower = format.Contains("-L");
            string result = "";

            foreach (var c in colorformat)
            {
                result += c switch
                {
                    'R' => Tohex(R),
                    'G' => Tohex(G),
                    'B' => Tohex(B),
                    'A' => Tohex(A),
                    _ => Throw(format)
                };
            }

            if (format.Contains("#")) result = "#" + result;
            else if (format.Contains("0X")) result = "0x" + result;

            if (islower) result = result.ToLowerInvariant();

            return result;
        }
        private static string Tohex(byte value) => value.ToString("X2");
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(A), A);
            info.AddValue(nameof(R), R);
            info.AddValue(nameof(G), G);
            info.AddValue(nameof(B), B);
        }

        public static implicit operator BGRA32(Color c)
            => new(c.r, c.g, c.b, c.a);
        public static implicit operator RGBA32(Color c)
            => new(c.r, c.g, c.b, c.a);
        public static implicit operator BGR24(Color c)
            => new(c.r, c.g, c.b);
        public static implicit operator RGB24(Color c)
            => new(c.r, c.g, c.b);

        public static bool operator ==(Color left, Color right)
            => left.Equals(right);
        public static bool operator !=(Color left, Color right)
            => !(left == right);
    }
}