using System;
using System.Runtime.Serialization;

using BEditor.Drawing.Resources;

namespace BEditor.Drawing
{
    [Serializable]
    public readonly struct Size : IEquatable<Size>, ISerializable
    {
        public static readonly Size Empty;

        public Size(int width, int height)
        {
            if (width < 0) throw new ArgumentOutOfRangeException(nameof(width), string.Format(Strings.LessThan, nameof(width), 0));
            if (height < 0) throw new ArgumentOutOfRangeException(nameof(height), string.Format(Strings.LessThan, nameof(height), 0));

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

        public float Aspect => Width / ((float)Height);

        public static Size Add(Size size1, Size size2)
        {
            return new(size1.Width + size2.Width, size1.Height + size2.Height);
        }

        public static Size Subtract(Size size1, Size size2)
        {
            return new(size1.Width - size2.Width, size1.Height - size2.Height);
        }

        public override bool Equals(object? obj)
        {
            return obj is Size size && Equals(size);
        }

        public bool Equals(Size other)
        {
            return Width == other.Width && Height == other.Height && Aspect == other.Aspect;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Width, Height, Aspect);
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(Width), Width);
            info.AddValue(nameof(Height), Height);
        }

        public static Size operator +(Size size1, Size size2)
        {
            return Add(size1, size2);
        }

        public static Size operator -(Size size1, Size size2)
        {
            return Subtract(size1, size2);
        }

        public static Size operator *(Size left, int right)
        {
            return new(left.Width * right, left.Height * right);
        }

        public static Size operator /(Size left, int right)
        {
            return new(left.Width / right, left.Height / right);
        }

        public static bool operator ==(Size left, Size right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Size left, Size right)
        {
            return !(left == right);
        }
    }
}
