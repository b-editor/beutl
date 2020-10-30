using System;
using System.Runtime.Serialization;

using BEditor.NET.SDL2;

namespace BEditor.NET.Media {
    /// <summary>
    /// 
    /// </summary>
    [DataContract(Namespace = "")]
    public struct Color3 : IEquatable<Color3> {

        #region StaticInit

        public static Color3 FromRgb(byte r = 255, byte g = 255, byte b = 255) {
            var color = new Color3 {
                R = r,
                G = g,
                B = b
            };

            return color;
        }
        public static Color3 FromRgb(float r = 255, float g = 255, float b = 255) {
            var color = new Color3 {
                R = r,
                G = g,
                B = b
            };

            return color;
        }

        #endregion

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is Color3 color && Equals(color);
        /// <inheritdoc/>
        public bool Equals(Color3 other) => R == other.R && G == other.G && B == other.B;
        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(R, G, B);
        /// <inheritdoc/>
        public override string ToString() => $"(Red:{R} Green:{G} Blue:{B})";

        #region Properties

        [DataMember(Order = 0)]
        public float R { get; set; }

        [DataMember(Order = 1)]
        public float G { get; set; }

        [DataMember(Order = 2)]
        public float B { get; set; }

        #endregion


        #region Colors

        public static readonly Color3 White = FromRgb(255, 255, 255);
        public static readonly Color3 Black = FromRgb(0, 0, 0);
        public static readonly Color3 Red = FromRgb(244, 67, 54);
        public static readonly Color3 Pink = FromRgb(233, 30, 99);
        public static readonly Color3 Purple = FromRgb(156, 39, 176);
        public static readonly Color3 DeepPurple = FromRgb(103, 58, 183);
        public static readonly Color3 Indigo = FromRgb(63, 81, 181);
        public static readonly Color3 Blue = FromRgb(33, 150, 243);
        public static readonly Color3 LightBlue = FromRgb(3, 169, 244);
        public static readonly Color3 Cyan = FromRgb(0, 188, 212);
        public static readonly Color3 Teal = FromRgb(0, 150, 136);
        public static readonly Color3 Green = FromRgb(76, 175, 80);
        public static readonly Color3 LightGreen = FromRgb(139, 195, 74);
        public static readonly Color3 Lime = FromRgb(205, 220, 57);
        public static readonly Color3 Yellow = FromRgb(255, 235, 59);
        public static readonly Color3 Amber = FromRgb(255, 193, 7);
        public static readonly Color3 Orange = FromRgb(255, 152, 0);
        public static readonly Color3 DeepOrange = FromRgb(255, 87, 34);
        public static readonly Color3 Brown = FromRgb(121, 85, 72);
        public static readonly Color3 Grey = FromRgb(158, 158, 158);
        public static readonly Color3 BlueGrey = FromRgb(96, 125, 139);

        #endregion

        #region キャスト

        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator byte[](Color3 c) => new byte[] { (byte)c.R, (byte)c.G, (byte)c.B };
        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator float[](Color3 c) => new float[] { c.R, c.G, c.B };
        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator System.Drawing.Color(Color3 c) => System.Drawing.Color.FromArgb((int)c.R, (int)c.G, (int)c.B);
#if OldOpenTK
        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator OpenTK.Graphics.Color4(Color3 c) => new OpenTK.Graphics.Color4((byte)c.R, (byte)c.G, (byte)c.B, 255);
#else
        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator OpenTK.Mathematics.Color4(Color3 c) => new OpenTK.Mathematics.Color4((byte)c.R, (byte)c.G, (byte)c.B, 255);
#endif
        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator Color4(Color3 c) => Color4.FromRgba((byte)c.R, (byte)c.G, (byte)c.B, 255);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator Color3(Color4 c) => FromRgb((byte)c.R, (byte)c.G, (byte)c.B);

        internal SDL.SDL_Color ToSDL() => new SDL.SDL_Color() { r = (byte)R, g = (byte)G, b = (byte)B, a = 255 };

        #endregion

        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator ==(Color3 left, Color3 right) => left.Equals(right);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator !=(Color3 left, Color3 right) => !(left == right);
    }

    /// <summary>
    /// 
    /// </summary>
    [DataContract(Namespace = "")]
    public struct Color4 : IEquatable<Color4> {

        #region StaticInit

        public static Color4 FromRgba(byte r = 255, byte g = 255, byte b = 255, byte a = 255) {
            var color = new Color4 {
                R = r,
                G = g,
                B = b,
                A = a
            };

            return color;
        }

        public static Color4 FromRgba(float r = 255, float g = 255, float b = 255, float a = 255) {
            var color = new Color4 {
                R = r,
                G = g,
                B = b,
                A = a
            };

            return color;
        }

        #endregion

        public override bool Equals(object obj) => obj is Color4 color && Equals(color);
        /// <inheritdoc/>
        public bool Equals(Color4 other) => R == other.R && G == other.G && B == other.B && A == other.A;
        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(R, G, B, A);
        /// <inheritdoc/>
        public override string ToString() => $"(Red:{R} Green:{G} Blue:{B} Alpha:{A})";

        #region Properties

        [DataMember(Order = 0)]
        public float R { get; set; }

        [DataMember(Order = 1)]
        public float G { get; set; }

        [DataMember(Order = 2)]
        public float B { get; set; }

        [DataMember(Order = 3)]
        public float A { get; set; }

        #endregion


        #region キャスト

        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator byte[](Color4 c) => new byte[] { (byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A };
        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator float[](Color4 c) => new float[] { c.R, c.G, c.B, c.A };
        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator System.Drawing.Color(Color4 c) => System.Drawing.Color.FromArgb((int)c.A, (int)c.R, (int)c.G, (int)c.B);
#if OldOpenTK
        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator OpenTK.Graphics.Color4(Color4 c) => new OpenTK.Graphics.Color4((byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator Color4(OpenTK.Graphics.Color4 c) => Color4.FromRgba((byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A);
#else
        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator OpenTK.Mathematics.Color4(Color4 c) => new OpenTK.Mathematics.Color4((byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator Color4(OpenTK.Mathematics.Color4 c) => Color4.FromRgba((byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A);
#endif

        internal SDL.SDL_Color ToSDL() => new SDL.SDL_Color() { r = (byte)R, g = (byte)G, b = (byte)B, a = (byte)A };

        #endregion

        #region Colors

        public static readonly Color4 White = FromRgba(255, 255, 255, 255);
        public static readonly Color4 Black = FromRgba(0, 0, 0, 255);
        public static readonly Color4 Red = FromRgba(244, 67, 54);
        public static readonly Color4 Pink = FromRgba(233, 30, 99);
        public static readonly Color4 Purple = FromRgba(156, 39, 176);
        public static readonly Color4 DeepPurple = FromRgba(103, 58, 183);
        public static readonly Color4 Indigo = FromRgba(63, 81, 181);
        public static readonly Color4 Blue = FromRgba(33, 150, 243);
        public static readonly Color4 LightBlue = FromRgba(3, 169, 244);
        public static readonly Color4 Cyan = FromRgba(0, 188, 212);
        public static readonly Color4 Teal = FromRgba(0, 150, 136);
        public static readonly Color4 Green = FromRgba(76, 175, 80);
        public static readonly Color4 LightGreen = FromRgba(139, 195, 74);
        public static readonly Color4 Lime = FromRgba(205, 220, 57);
        public static readonly Color4 Yellow = FromRgba(255, 235, 59);
        public static readonly Color4 Amber = FromRgba(255, 193, 7);
        public static readonly Color4 Orange = FromRgba(255, 152, 0);
        public static readonly Color4 DeepOrange = FromRgba(255, 87, 34);
        public static readonly Color4 Brown = FromRgba(121, 85, 72);
        public static readonly Color4 Grey = FromRgba(158, 158, 158);
        public static readonly Color4 BlueGrey = FromRgba(96, 125, 139);

        #endregion

        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator ==(Color4 left, Color4 right) => left.Equals(right);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator !=(Color4 left, Color4 right) => !(left == right);
    }
}
