using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Media
{
    /// <summary>
    /// RGBA (red, green, blue, alpha) の色を表します
    /// </summary>
    [DataContract(Namespace = "")]
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ReadOnlyColor : IEquatable<ReadOnlyColor>
    {
        private readonly float scR;
        private readonly float scG;
        private readonly float scB;
        private readonly float scA;

        /// <summary>
        /// <see cref="ReadOnlyColor"/> 構造体の新しいインスタンスを初期化します
        /// </summary>
        /// <param name="r"><see cref="R"/> の値</param>
        /// <param name="g"><see cref="G"/> の値</param>
        /// <param name="b"><see cref="B"/> の値</param>
        /// <param name="a"><see cref="A"/> の値</param>
        public ReadOnlyColor(byte r = 255, byte g = 255, byte b = 255, byte a = 255)
        {
            scR = (float)r / 255;
            scG = (float)g / 255;
            scB = (float)b / 255;
            scA = (float)a / 255;
        }
        /// <summary>
        /// <see cref="ReadOnlyColor"/> 構造体の新しいインスタンスを初期化します
        /// </summary>
        /// <param name="r"><see cref="ScR"/> の値</param>
        /// <param name="g"><see cref="ScG"/> の値</param>
        /// <param name="b"><see cref="ScB"/> の値</param>
        /// <param name="a"><see cref="ScA"/> の値</param>
        public ReadOnlyColor(float r = 1, float g = 1, float b = 1, float a = 1)
        {
            scR = r;
            scG = g;
            scB = b;
            scA = a;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is ReadOnlyColor color && Equals(color);
        /// <inheritdoc/>
        public bool Equals(ReadOnlyColor other) => ScR == other.ScR && ScG == other.ScG && ScB == other.ScB && ScA == other.ScA;
        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(ScR, ScG, ScB, ScA);
        /// <inheritdoc/>
        public override string ToString() => $"(Red:{R} Green:{G} Blue:{B} Alpha:{A})";

        #region Properties

        /// <summary>
        /// 赤の値を取得または設定します
        /// </summary>
        public byte R => (byte)(ScR * 255);

        /// <summary>
        /// 緑の値を取得または設定します
        /// </summary>
        public byte G => (byte)(ScG * 255);

        /// <summary>
        /// 青の値を取得または設定します
        /// </summary>
        public byte B => (byte)(ScB * 255);

        /// <summary>
        /// アルファの値を取得または設定します
        /// </summary>
        public byte A => (byte)(ScA * 255);

        /// <summary>
        /// 赤の値を取得または設定します
        /// </summary>
        [DataMember(Order = 0)]
        public float ScR => scR;

        /// <summary>
        /// 緑の値を取得または設定します
        /// </summary>
        [DataMember(Order = 1)]
        public float ScG => scG;

        /// <summary>
        /// 青の値を取得または設定します
        /// </summary>
        [DataMember(Order = 2)]
        public float ScB => scB;

        /// <summary>
        /// アルファの値を取得または設定します
        /// </summary>
        [DataMember(Order = 3)]
        public float ScA => scA;

        #endregion


        #region キャスト

        /// <summary>
        /// <see cref="ReadOnlyColor"/> 構造体から <see cref="byte"/> の配列を作成します
        /// </summary>
        public static implicit operator byte[](ReadOnlyColor c) => new byte[] { c.R, c.G, c.B, c.A };

        /// <summary>
        /// <see cref="ReadOnlyColor"/> 構造体から <see cref="float"/>　の配列を作成します
        /// </summary>
        public static implicit operator float[](ReadOnlyColor c) => new float[] { c.ScR, c.ScG, c.ScB, c.ScA };
        /// <summary>
        /// <see cref="ReadOnlyColor"/> 構造体から <see cref="System.Drawing.Color"/> を作成します
        /// </summary>
        public static implicit operator System.Drawing.Color(ReadOnlyColor c) => System.Drawing.Color.FromArgb(c.A, c.R, c.G, c.B);
        /// <summary>
        /// <see cref="ReadOnlyColor"/> 構造体から <see cref="Color"/> を作成します
        /// </summary>
        public static implicit operator Color(ReadOnlyColor c) => new(c.ScR, c.ScG, c.ScB, c.ScA);
        /// <summary>
        /// <see cref="Color"/> 構造体から <see cref="ReadOnlyColor"/> を作成します
        /// </summary>
        public static implicit operator ReadOnlyColor(Color c) => new(c.ScR, c.ScG, c.ScB, c.ScA);
#if OldOpenTK
        /// <summary>
        /// <see cref="ReadOnlyColor"/> 構造体から <see cref="OpenTK.Graphics.Color4"/> を作成します
        /// </summary>
        public static implicit operator OpenTK.Graphics.Color4(ReadOnlyColor c) => new OpenTK.Graphics.Color4(c.R, c.G, c.B, c.A);
        /// <summary>
        /// <see cref="OpenTK.Graphics.Color4"/> 構造体から <see cref="ReadOnlyColor"/> を作成します
        /// </summary>
        /// <param name="c"></param>
        public static implicit operator ReadOnlyColor(OpenTK.Graphics.Color4 c) => new((byte)c.R, (byte)c.G, (byte)c.B, (byte)c.A);
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
        /// 2つの <see cref="ReadOnlyColor"/> 構造体を比較します
        /// </summary>
        /// <returns>2つの <see cref="ReadOnlyColor"/> 構造体が等しい場合は <see langword="true"/> 、そうでない場合は <see langword="false"/> です</returns>
        public static bool operator ==(ReadOnlyColor left, ReadOnlyColor right) => left.Equals(right);
        /// <summary>
        /// 2つの <see cref="ReadOnlyColor"/> 構造体を比較します
        /// </summary>
        /// <returns>2つの <see cref="ReadOnlyColor"/> 構造体が異なる場合は <see langword="true"/> 、そうでない場合は <see langword="false"/> です</returns>
        public static bool operator !=(ReadOnlyColor left, ReadOnlyColor right) => !(left == right);
    }
}
