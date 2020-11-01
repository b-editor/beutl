using System;
using System.Runtime.Serialization;

using BEditor.Core.SDL2;

namespace BEditor.Core.Media {
    /// <summary>
    /// 
    /// </summary>
    [DataContract(Namespace = "")]
    public struct Color : IEquatable<Color> {
        public Color(byte r = 255, byte g = 255, byte b = 255, byte a = 255) {
            ScR = r / 255;
            ScG = g / 255;
            ScB = b / 255;
            ScA = a / 255;
        }
        public Color(float r = 1, float g = 1, float b = 1, float a = 1) {
            ScR = r;
            ScG = g;
            ScB = b;
            ScA = a;
        }

        public override bool Equals(object obj) => obj is Color color && Equals(color);
        /// <inheritdoc/>
        public bool Equals(Color other) => R == other.R && G == other.G && B == other.B && A == other.A;
        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(R, G, B, A);
        /// <inheritdoc/>
        public override string ToString() => $"(Red:{R} Green:{G} Blue:{B} Alpha:{A})";

        #region Properties

        public byte R => (byte)(ScR * 255);

        public byte G => (byte)(ScG * 255);

        public byte B => (byte)(ScB * 255);

        public byte A => (byte)(ScA * 255);

        [DataMember(Order = 0)]
        public float ScR { get; set; }

        [DataMember(Order = 1)]
        public float ScG { get; set; }

        [DataMember(Order = 2)]
        public float ScB { get; set; }

        [DataMember(Order = 3)]
        public float ScA { get; set; }

        #endregion


        #region キャスト

        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator byte[](Color c) => new byte[] { (byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A };
        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator float[](Color c) => new float[] { c.R, c.G, c.B, c.A };
        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator System.Drawing.Color(Color c) => System.Drawing.Color.FromArgb((int)c.A, (int)c.R, (int)c.G, (int)c.B);
#if OldOpenTK
        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator OpenTK.Graphics.Color4(Color c) => new OpenTK.Graphics.Color4((byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator Color(OpenTK.Graphics.Color4 c) => new((byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A);
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
        public static implicit operator Color4(OpenTK.Mathematics.Color4 c) => Color4.new((byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A);
#endif

        internal SDL.SDL_Color ToSDL() => new SDL.SDL_Color() { r = (byte)R, g = (byte)G, b = (byte)B, a = (byte)A };

        #endregion

        #region Colors

        public static readonly Color White = new(255, 255, 255, 255);
        public static readonly Color Black = new(0, 0, 0, 255);
        public static readonly Color Red = new(244, 67, 54);
        public static readonly Color Pink = new(233, 30, 99);
        public static readonly Color Purple = new(156, 39, 176);
        public static readonly Color DeepPurple = new(103, 58, 183);
        public static readonly Color Indigo = new(63, 81, 181);
        public static readonly Color Blue = new(33, 150, 243);
        public static readonly Color LightBlue = new(3, 169, 244);
        public static readonly Color Cyan = new(0, 188, 212);
        public static readonly Color Teal = new(0, 150, 136);
        public static readonly Color Green = new(76, 175, 80);
        public static readonly Color LightGreen = new(139, 195, 74);
        public static readonly Color Lime = new(205, 220, 57);
        public static readonly Color Yellow = new(255, 235, 59);
        public static readonly Color Amber = new(255, 193, 7);
        public static readonly Color Orange = new(255, 152, 0);
        public static readonly Color DeepOrange = new(255, 87, 34);
        public static readonly Color Brown = new(121, 85, 72);
        public static readonly Color Grey = new(158, 158, 158);
        public static readonly Color BlueGrey = new(96, 125, 139);

        #endregion

        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator ==(Color left, Color right) => left.Equals(right);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator !=(Color left, Color right) => !(left == right);
    }
}