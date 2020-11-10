using System;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace BEditor.Core.Media {
    /// <summary>
    /// RGBA (red, green, blue, alpha) の色を表します
    /// </summary>
    [DataContract(Namespace = "")]
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Color : IEquatable<Color> {
        private float scR;
        private float scG;
        private float scB;
        private float scA;
        #region Colors

        /// <summary>
        /// Red:255 
        /// Green:255 
        /// Blue:255 
        /// Alpha:255 
        /// </summary>
        public static readonly Color White = new(255, 255, 255, 255);
        /// <summary>
        /// Red:0 
        /// Green:0 
        /// Blue:0 
        /// Alpha:255 
        /// </summary>
        public static readonly Color Black = new(0, 0, 0, 255);
        /// <summary>
        /// Red:244 
        /// Green:67
        /// Blue:54 
        /// Alpha:255 
        /// </summary>
        public static readonly Color Red = new(244, 67, 54);
        /// <summary>
        /// Red:233 
        /// Green:30 
        /// Blue:99 
        /// Alpha:255 
        /// </summary>
        public static readonly Color Pink = new(233, 30, 99);
        /// <summary>
        /// Red:156 
        /// Green:39 
        /// Blue:176 
        /// Alpha:255 
        /// </summary>
        public static readonly Color Purple = new(156, 39, 176);
        /// <summary>
        /// Red:103 
        /// Green:58 
        /// Blue:182 
        /// Alpha:255 
        /// </summary>
        public static readonly Color DeepPurple = new(103, 58, 183);
        /// <summary>
        /// Red:63 
        /// Green:81 
        /// Blue:181 
        /// Alpha:255 
        /// </summary>
        public static readonly Color Indigo = new(63, 81, 181);
        /// <summary>
        /// Red:33 
        /// Green:150 
        /// Blue:243 
        /// Alpha:255 
        /// </summary>
        public static readonly Color Blue = new(33, 150, 243);
        /// <summary>
        /// Red:3 
        /// Green:169 
        /// Blue:244 
        /// Alpha:255 
        /// </summary>
        public static readonly Color LightBlue = new(3, 169, 244);
        /// <summary>
        /// Red:0 
        /// Green:188 
        /// Blue:212 
        /// Alpha:255 
        /// </summary>
        public static readonly Color Cyan = new(0, 188, 212);
        /// <summary>
        /// Red:0 
        /// Green:150 
        /// Blue:136 
        /// Alpha:255 
        /// </summary>
        public static readonly Color Teal = new(0, 150, 136);
        /// <summary>
        /// Red:76 
        /// Green:175 
        /// Blue:80 
        /// Alpha:255 
        /// </summary>
        public static readonly Color Green = new(76, 175, 80);
        /// <summary>
        /// Red:139 
        /// Green:195 
        /// Blue:74 
        /// Alpha:255 
        /// </summary>
        public static readonly Color LightGreen = new(139, 195, 74);
        /// <summary>
        /// Red:205 
        /// Green:220 
        /// Blue:57 
        /// Alpha:255 
        /// </summary>
        public static readonly Color Lime = new(205, 220, 57);
        /// <summary>
        /// Red:255 
        /// Green:235 
        /// Blue:59 
        /// Alpha:255 
        /// </summary>
        public static readonly Color Yellow = new(255, 235, 59);
        /// <summary>
        /// Red:255 
        /// Green:193 
        /// Blue:7 
        /// Alpha:255 
        /// </summary>
        public static readonly Color Amber = new(255, 193, 7);
        /// <summary>
        /// Red:255 
        /// Green:152 
        /// Blue:0 
        /// Alpha:255 
        /// </summary>
        public static readonly Color Orange = new(255, 152, 0);
        /// <summary>
        /// Red:255 
        /// Green:87 
        /// Blue:34 
        /// Alpha:255 
        /// </summary>
        public static readonly Color DeepOrange = new(255, 87, 34);
        /// <summary>
        /// Red:121 
        /// Green:85 
        /// Blue:72 
        /// Alpha:255 
        /// </summary>
        public static readonly Color Brown = new(121, 85, 72);
        /// <summary>
        /// Red:158 
        /// Green:158 
        /// Blue:158 
        /// Alpha:255 
        /// </summary>
        public static readonly Color Grey = new(158, 158, 158);
        /// <summary>
        /// Red:96 
        /// Green:125 
        /// Blue:139 
        /// Alpha:255 
        /// </summary>
        public static readonly Color BlueGrey = new(96, 125, 139);

        #endregion

        /// <summary>
        /// <see cref="Color"/> 構造体の新しいインスタンスを初期化します
        /// </summary>
        /// <param name="r"><see cref="R"/> の値</param>
        /// <param name="g"><see cref="G"/> の値</param>
        /// <param name="b"><see cref="B"/> の値</param>
        /// <param name="a"><see cref="A"/> の値</param>
        public Color(byte r = 255, byte g = 255, byte b = 255, byte a = 255) {
            scR = (float)r / 255;
            scG = (float)g / 255;
            scB = (float)b / 255;
            scA = (float)a / 255;
        }
        /// <summary>
        /// <see cref="Color"/> 構造体の新しいインスタンスを初期化します
        /// </summary>
        /// <param name="r"><see cref="ScR"/> の値</param>
        /// <param name="g"><see cref="ScG"/> の値</param>
        /// <param name="b"><see cref="ScB"/> の値</param>
        /// <param name="a"><see cref="ScA"/> の値</param>
        public Color(float r = 1, float g = 1, float b = 1, float a = 1) {
            scR = r;
            scG = g;
            scB = b;
            scA = a;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is Color color && Equals(color);
        /// <inheritdoc/>
        public bool Equals(Color other) => R == other.R && G == other.G && B == other.B && A == other.A;
        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(R, G, B, A);
        /// <inheritdoc/>
        public override string ToString() => $"(Red:{R} Green:{G} Blue:{B} Alpha:{A})";

        #region Properties

        /// <summary>
        /// 赤の値を取得または設定します
        /// </summary>
        public byte R {
            get => (byte)(ScR * 255);
            set => ScR = (float)value / 255;
        }

        /// <summary>
        /// 緑の値を取得または設定します
        /// </summary>
        public byte G {
            get => (byte)(ScG * 255);
            set => ScG = (float)value / 255;
        }

        /// <summary>
        /// 青の値を取得または設定します
        /// </summary>
        public byte B {
            get => (byte)(ScB * 255);
            set => ScB = (float)value / 255;
        }

        /// <summary>
        /// アルファの値を取得または設定します
        /// </summary>
        public byte A {
            get => (byte)(ScA * 255);
            set => ScA = (float)value / 255;
        }

        /// <summary>
        /// 赤の値を取得または設定します
        /// </summary>
        [DataMember(Order = 0)]
        public float ScR { get => scR; set => scR = value; }

        /// <summary>
        /// 緑の値を取得または設定します
        /// </summary>
        [DataMember(Order = 1)]
        public float ScG { get => scG; set => scG = value; }

        /// <summary>
        /// 青の値を取得または設定します
        /// </summary>
        [DataMember(Order = 2)]
        public float ScB { get => scB; set => scB = value; }

        /// <summary>
        /// アルファの値を取得または設定します
        /// </summary>
        [DataMember(Order = 3)]
        public float ScA { get => scA; set => scA = value; }

        #endregion


        #region キャスト

        /// <summary>
        /// <see cref="Color"/> 構造体から <see cref="byte"/> の配列を作成します
        /// </summary>
        public static implicit operator byte[](Color c) => new byte[] { c.R, c.G, c.B, c.A };

        /// <summary>
        /// <see cref="Color"/> 構造体から <see cref="float"/>　の配列を作成します
        /// </summary>
        public static implicit operator float[](Color c) => new float[] { c.ScR, c.ScG, c.ScB, c.ScA };
        /// <summary>
        /// <see cref="Color"/> 構造体から <see cref="System.Drawing.Color"/> を作成します
        /// </summary>
        public static implicit operator System.Drawing.Color(Color c) => System.Drawing.Color.FromArgb((int)c.A, (int)c.R, (int)c.G, (int)c.B);
#if OldOpenTK
        /// <summary>
        /// <see cref="Color"/> 構造体から <see cref="OpenTK.Graphics.Color4"/> を作成します
        /// </summary>
        public static implicit operator OpenTK.Graphics.Color4(Color c) => new OpenTK.Graphics.Color4((byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A);
        /// <summary>
        /// <see cref="OpenTK.Graphics.Color4"/> 構造体から <see cref="Color"/> を作成します
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator Color(OpenTK.Graphics.Color4 c) => new((byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A);
#else
        /// <summary>
        /// <see cref="Color"/> 構造体から <see cref="OpenTK.Mathematics.Color4"/> を作成します
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator OpenTK.Mathematics.Color4(Color c) => new(c.R, c.G, c.B, c.A);
        /// <summary>
        /// <see cref="OpenTK.Mathematics.Color4"/> 構造体から <see cref="Color"/> を作成します
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator Color(OpenTK.Mathematics.Color4 c) => new((byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A);
#endif

        #endregion

        /// <summary>
        /// 2つの <see cref="Color"/> 構造体を比較します
        /// </summary>
        /// <returns>2つの <see cref="Color"/> 構造体が等しい場合は <see langword="true"/> 、そうでない場合は <see langword="false"/> です</returns>
        public static bool operator ==(Color left, Color right) => left.Equals(right);
        /// <summary>
        /// 2つの <see cref="Color"/> 構造体を比較します
        /// </summary>
        /// <returns>2つの <see cref="Color"/> 構造体が異なる場合は <see langword="true"/> 、そうでない場合は <see langword="false"/> です</returns>
        public static bool operator !=(Color left, Color right) => !(left == right);
    }
}