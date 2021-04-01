using System;
using System.Runtime.Serialization;

namespace BEditor.Drawing
{
    [Serializable]
    public readonly struct PointF : IEquatable<PointF>, ISerializable
    {
        public static readonly PointF Empty;

        public PointF(float x, float y)
        {
            X = x;
            Y = y;
        }

        private PointF(SerializationInfo info, StreamingContext context)
        {
            X = info.GetSingle(nameof(X));
            Y = info.GetSingle(nameof(Y));
        }

        public float X { get; }

        public float Y { get; }

        public static PointF Add(PointF point, Size size)
        {
            return new(point.X + size.Width, point.Y + size.Height);
        }

        public static PointF Add(PointF point1, PointF point2)
        {
            return new(point1.X + point2.X, point1.Y + point2.Y);
        }

        public static PointF Subtract(PointF point, Size size)
        {
            return new(point.X - size.Width, point.Y - size.Height);
        }

        public static PointF Subtract(PointF point1, PointF point2)
        {
            return new(point1.X - point2.X, point1.Y - point2.Y);
        }

        public override bool Equals(object? obj)
        {
            return obj is PointF point && Equals(point);
        }

        public bool Equals(PointF other)
        {
            return X == other.X && Y == other.Y;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y);
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(X), X);
            info.AddValue(nameof(Y), Y);
        }

        public static PointF operator +(PointF point1, PointF point2)
        {
            return Add(point1, point2);
        }

        public static PointF operator +(PointF point, Size size)
        {
            return Add(point, size);
        }

        public static PointF operator -(PointF point1, PointF point2)
        {
            return Subtract(point1, point2);
        }

        public static PointF operator -(PointF point, Size size)
        {
            return Subtract(point, size);
        }

        public static PointF operator *(PointF left, PointF right)
        {
            return new(left.X * right.X, left.Y * right.Y);
        }

        public static PointF operator /(PointF left, PointF right)
        {
            return new(left.X / right.X, left.Y / right.Y);
        }

        public static PointF operator *(PointF left, int right)
        {
            return new(left.X * right, left.Y * right);
        }

        public static PointF operator /(PointF left, int right)
        {
            return new(left.X / right, left.Y / right);
        }

        public static bool operator ==(PointF left, PointF right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PointF left, PointF right)
        {
            return !(left == right);
        }
    }
}
