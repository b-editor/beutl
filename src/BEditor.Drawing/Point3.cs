using System;
using System.Runtime.Serialization;

namespace BEditor.Drawing
{
    [Serializable]
    public readonly struct Point3 : IEquatable<Point3>, ISerializable
    {
        public static readonly Point3 Empty;

        public Point3(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }
        private Point3(SerializationInfo info, StreamingContext context)
        {
            X = info.GetInt32(nameof(X));
            Y = info.GetInt32(nameof(Y));
            Z = info.GetInt32(nameof(Z));
        }

        public int X { get; }
        public int Y { get; }
        public int Z { get; }

        public static Point3 Add(Point3 point1, Point3 point2)
            => new(point1.X + point2.X, point1.Y + point2.Y, point1.Z + point2.Z);
        public static Point3 Subtract(Point3 point1, Point3 point2)
            => new(point1.X - point2.X, point1.Y - point2.Y, point1.Z - point2.Z);
        public override bool Equals(object? obj)
            => obj is Point3 point && Equals(point);
        public bool Equals(Point3 other)
            => X == other.X && Y == other.Y && Z == other.Z;
        public override int GetHashCode()
            => HashCode.Combine(X, Y, Z);
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(X), X);
            info.AddValue(nameof(Y), Y);
            info.AddValue(nameof(Z), Z);
        }

        public static Point3 operator +(Point3 point1, Point3 point2)
            => Add(point1, point2);
        public static Point3 operator -(Point3 point1, Point3 point2)
            => Subtract(point1, point2);
        public static Point3 operator *(Point3 left, Point3 right)
            => new(left.X * right.X, left.Y * right.Y, left.Z * right.Z);
        public static Point3 operator /(Point3 left, Point3 right)
            => new(left.X / right.X, left.Y / right.Y, left.Z / right.Z);
        public static Point3 operator *(Point3 left, int right)
            => new(left.X * right, left.Y * right, left.Z * right);
        public static Point3 operator /(Point3 left, int right)
            => new(left.X / right, left.Y / right, left.Z / right);
        public static bool operator ==(Point3 left, Point3 right)
            => left.Equals(right);
        public static bool operator !=(Point3 left, Point3 right)
            => !(left == right);
    }
}
