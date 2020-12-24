using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Drawing
{
    [Serializable]
    public readonly struct Point : IEquatable<Point>
    {
        public static readonly Point Empty;

        public Point(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }
        public int Y { get; }

        public static Point Add(Point point, Size size)
            => new(point.X + size.Width, point.Y + size.Height);
        public static Point Add(Point point1, Point point2)
            => new(point1.X + point2.X, point1.Y + point2.Y);
        public static Point Subtract(Point point, Size size)
            => new(point.X - size.Width, point.Y - size.Height);
        public static Point Subtract(Point point1, Point point2)
            => new(point1.X - point2.X, point1.Y - point2.Y);
        public override bool Equals(object? obj)
            => obj is Point point && Equals(point);
        public bool Equals(Point other)
            => X == other.X && Y == other.Y;
        public override int GetHashCode()
            => HashCode.Combine(X, Y);

        public static Point operator +(Point point1, Point point2)
            => Add(point1, point2);
        public static Point operator +(Point point, Size size)
            => Add(point, size);
        public static Point operator -(Point point1, Point point2)
            => Subtract(point1, point2);
        public static Point operator -(Point point, Size size)
            => Subtract(point, size);
        public static Point operator *(Point left, Point right)
            => new(left.X * right.X, left.Y * right.Y);
        public static Point operator /(Point left, Point right)
            => new(left.X / right.X, left.Y / right.Y);
        public static Point operator *(Point left, int right)
            => new(left.X * right, left.Y * right);
        public static Point operator /(Point left, int right)
            => new(left.X / right, left.Y / right);
        public static bool operator ==(Point left, Point right)
            => left.Equals(right);
        public static bool operator !=(Point left, Point right)
            => !(left == right);
    }
}
