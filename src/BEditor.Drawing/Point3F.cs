using System;
using System.Runtime.Serialization;

namespace BEditor.Drawing
{
    [Serializable]
    public readonly struct Point3F : IEquatable<Point3F>, ISerializable
    {
        public static readonly Point3F Empty;

        public Point3F(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
        private Point3F(SerializationInfo info, StreamingContext context)
        {
            X = info.GetSingle(nameof(X));
            Y = info.GetSingle(nameof(Y));
            Z = info.GetSingle(nameof(Z));
        }

        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public static Point3F Add(Point3F point1, Point3F point2)
            => new(point1.X + point2.X, point1.Y + point2.Y, point1.Z + point2.Z);
        public static Point3F Subtract(Point3F point1, Point3F point2)
            => new(point1.X - point2.X, point1.Y - point2.Y, point1.Z - point2.Z);
        public override bool Equals(object? obj)
            => obj is Point3F point && Equals(point);
        public bool Equals(Point3F other)
            => X == other.X && Y == other.Y && Z == other.Z;
        public override int GetHashCode()
            => HashCode.Combine(X, Y, Z);
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(X), X);
            info.AddValue(nameof(Y), Y);
            info.AddValue(nameof(Z), Z);
        }

        public static Point3F operator +(Point3F point1, Point3F point2)
            => Add(point1, point2);
        public static Point3F operator -(Point3F point1, Point3F point2)
            => Subtract(point1, point2);
        public static Point3F operator *(Point3F left, Point3F right)
            => new(left.X * right.X, left.Y * right.Y, left.Z * right.Z);
        public static Point3F operator /(Point3F left, Point3F right)
            => new(left.X / right.X, left.Y / right.Y, left.Z / right.Z);
        public static Point3F operator *(Point3F left, int right)
            => new(left.X * right, left.Y * right, left.Z * right);
        public static Point3F operator /(Point3F left, int right)
            => new(left.X / right, left.Y / right, left.Z / right);
        public static bool operator ==(Point3F left, Point3F right)
            => left.Equals(right);
        public static bool operator !=(Point3F left, Point3F right)
            => !(left == right);
    }
}
