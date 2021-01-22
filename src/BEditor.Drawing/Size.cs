using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Properties;

namespace BEditor.Drawing
{
    [Serializable]
    public readonly struct Size : IEquatable<Size>, ISerializable
    {
        public static readonly Size Empty;

        public Size(int width, int height)
        {
            if (width < 0) throw new ArgumentOutOfRangeException(nameof(width), string.Format(Resources.LessThan, nameof(width), 0));
            if (height < 0) throw new ArgumentOutOfRangeException(nameof(height), string.Format(Resources.LessThan, nameof(height), 0));

            Width = width;
            Height = height;
        }
        private Size(SerializationInfo info, StreamingContext context)
        {
            Width = info.GetInt32(nameof(Width));
            Height = info.GetInt32(nameof(Height));
        }

        public int Width { get; }
        public int Height { get; }
        public float Aspect
            => ((float)Width) / ((float)Height);

        public static Size Add(Size size1, Size size2)
            => new(size1.Width + size2.Width, size1.Height + size2.Height);
        public static Size Subtract(Size size1, Size size2)
            => new(size1.Width - size2.Width, size1.Height - size2.Height);
        public override bool Equals(object? obj)
            => obj is Size size && Equals(size);
        public bool Equals(Size other)
            => Width == other.Width && Height == other.Height && Aspect == other.Aspect;
        public override int GetHashCode()
            => HashCode.Combine(Width, Height, Aspect);
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(Width), Width);
            info.AddValue(nameof(Height), Height);
        }

        public static Size operator +(Size size1, Size size2)
            => Add(size1, size2);
        public static Size operator -(Size size1, Size size2)
            => Subtract(size1, size2);
        public static Size operator *(Size left, int right)
            => new(left.Width * right, left.Height * right);
        public static Size operator /(Size left, int right)
            => new(left.Width / right, left.Height / right);
        public static bool operator ==(Size left, Size right)
            => left.Equals(right);
        public static bool operator !=(Size left, Size right)
            => !(left == right);
    }
}
