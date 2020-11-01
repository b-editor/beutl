using System;
using System.Runtime.Serialization;

namespace BEditor.Core.Media {
#nullable enable
    /// <summary>
    /// 
    /// </summary>
    [DataContract(Namespace = "")]
    public struct Size : IEquatable<Size> {
        /// <summary>
        /// 
        /// </summary>
        public static readonly Size Empty;
        private int width;
        private int height;

        /// <summary>
        /// 
        /// </summary>
        [DataMember(Order = 0)]
        public int Width {
            get => width;
            set {
                if (value < 0) throw new Exception("Width < 0");

                width = value;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        [DataMember(Order = 1)]
        public int Height {
            get => height;
            set {
                if (value < 0) throw new Exception("Height < 0");

                height = value;
            }
        }
        /// <summary>
        /// 
        /// </summary>
        public float Aspect => width / height;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="width"></param>
        /// <param name="height"></param>
        public Size(int width, int height) {
            if (width < 0) throw new Exception("Width < 0");
            if (height < 0) throw new Exception("Height < 0");

            this.width = width;
            this.height = height;
        }

        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="size1"></param>
        /// <param name="size2"></param>
        /// <returns></returns>
        public static Size Add(Size size1, Size size2) => new Size() {
            Width = size1.Width + size2.Width,
            Height = size1.Height + size2.Height
        };
        /// <summary>
        /// 
        /// </summary>
        /// <param name="size1"></param>
        /// <param name="size2"></param>
        /// <returns></returns>
        public static Size Subtract(Size size1, Size size2) => new Size() {
            Width = size1.Width - size2.Width,
            Height = size1.Height - size2.Height
        };

        /// <inheritdoc/>
        public bool Equals(Size other) => Width == other.Width && Height == other.Height;
        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Size other && Equals(other);
        /// <inheritdoc/>
        public override string ToString() => $"(Width:{Width} Height:{Height})";
        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Width, Height);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="size1"></param>
        /// <param name="size2"></param>
        /// <returns></returns>
        public static Size operator +(Size size1, Size size2) => Add(size1, size2);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="size1"></param>
        /// <param name="size2"></param>
        /// <returns></returns>
        public static Size operator -(Size size1, Size size2) => Subtract(size1, size2);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static Size operator *(Size left, int right) => new Size(left.Width * right, left.Height * right);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static Size operator /(Size left, int right) => new Size(left.Width / right, left.Height / right);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator ==(Size left, Size right) => left.Equals(right);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator !=(Size left, Size right) => !left.Equals(right);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rect"></param>
        public static explicit operator System.Drawing.Size(Size rect) => new System.Drawing.Size(rect.Width, rect.Height);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="rect"></param>
        public static explicit operator Size(System.Drawing.Size rect) => new Size(rect.Width, rect.Height);
    }
}
