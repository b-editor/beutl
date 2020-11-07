using System;
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;

namespace BEditor.Core.Media {
#nullable enable
    /// <summary>
    /// 
    /// </summary>
    [DataContract(Namespace = "")]
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Size : IEquatable<Size> {
        /// <summary>
        /// 
        /// </summary>
        public static readonly Size Empty;

        /// <summary>
        /// 
        /// </summary>
        [DataMember(Order = 0)]
        public int Width { get; set; }
        /// <summary>
        /// 
        /// </summary>
        [DataMember(Order = 1)]
        public int Height { get; set; }
        /// <summary>
        /// 
        /// </summary>
        public float Aspect => ((float)Width) / ((float)Height);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        public Size(int width, int height) {
            if (width < 0) throw new Exception("Width < 0");
            if (height < 0) throw new Exception("Height < 0");

            Width = width;
            Height = height;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="size1"></param>
        /// <param name="size2"></param>
        /// <returns></returns>
        [Pure]
        public static Size Add(Size size1, Size size2) => new Size(size1.Width + size2.Width, size1.Height + size2.Height);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="size1"></param>
        /// <param name="size2"></param>
        /// <returns></returns>
        [Pure]
        public static Size Subtract(Size size1, Size size2) => new Size(size1.Width - size2.Width, size1.Height - size2.Height);

        /// <inheritdoc/>
        [Pure]
        public bool Equals(Size other) => Width == other.Width && Height == other.Height;
        /// <inheritdoc/>
        [Pure]
        public override bool Equals(object? obj) => obj is Size other && Equals(other);
        /// <inheritdoc/>
        [Pure]
        public override string ToString() => $"(Width:{Width} Height:{Height})";
        /// <inheritdoc/>
        [Pure]
        public override int GetHashCode() => HashCode.Combine(Width, Height);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="size1"></param>
        /// <param name="size2"></param>
        /// <returns></returns>
        [Pure]
        public static Size operator +(Size size1, Size size2) => Add(size1, size2);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="size1"></param>
        /// <param name="size2"></param>
        /// <returns></returns>
        [Pure]
        public static Size operator -(Size size1, Size size2) => Subtract(size1, size2);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        [Pure]
        public static Size operator *(Size left, int right) => new Size(left.Width * right, left.Height * right);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        [Pure]
        public static Size operator /(Size left, int right) => new Size(left.Width / right, left.Height / right);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        [Pure]
        public static bool operator ==(Size left, Size right) => left.Equals(right);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        [Pure]
        public static bool operator !=(Size left, Size right) => !left.Equals(right);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rect"></param>
        [Pure]
        public static explicit operator System.Drawing.Size(Size rect) => new System.Drawing.Size(rect.Width, rect.Height);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="rect"></param>
        [Pure]
        public static explicit operator Size(System.Drawing.Size rect) => new Size(rect.Width, rect.Height);
    }
}
