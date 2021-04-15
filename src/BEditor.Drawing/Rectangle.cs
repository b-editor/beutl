using System;
using System.Runtime.Serialization;

using BEditor.Drawing.Resources;

namespace BEditor.Drawing
{
    [Serializable]
    public readonly struct Rectangle : IEquatable<Rectangle>, ISerializable
    {
        public static readonly Rectangle Empty;

        public Rectangle(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public Rectangle(Point point, Size size)
        {
            X = point.X;
            Y = point.Y;
            Width = size.Width;
            Height = size.Height;
        }

        public int X { get; }

        public int Y { get; }

        public int Width { get; }

        public int Height { get; }

        public int Top => Y;

        public int Bottom => Y + Height;

        public int Left => X;

        public int Right => X + Width;

        public Point TopLeft => new(X, Y);

        public Point BottomRight => new(X + Width, Y + Height);

        public Point Point => new(X, Y);

        public Size Size => new(Width, Height);

        public static Rectangle FromLTRB(int left, int top, int right, int bottom)
        {
            var r = new Rectangle(
                x: left,
                y: top,
                width: right - left,
                height: bottom - top);

            if (r.Width < 0)
            {
                throw new ArgumentException(string.Format(Strings.LessThan, nameof(left), nameof(right)));
            }
            if (r.Height < 0)
            {
                throw new ArgumentException(string.Format(Strings.LessThan, nameof(top), nameof(bottom)));
            }

            return r;
        }

        public static Rectangle Inflate(Rectangle rect, int x, int y)
        {
            return new(
                rect.X - x,
                rect.Y - y,
                rect.Width + (2 * x),
                rect.Height + (2 * y));
        }

        public static Rectangle Intersect(Rectangle a, Rectangle b)
        {
            var x1 = Math.Max(a.X, b.X);
            var x2 = Math.Min(a.X + a.Width, b.X + b.Width);
            var y1 = Math.Max(a.Y, b.Y);
            var y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

            if (x2 >= x1 && y2 >= y1)
            {
                return new Rectangle(x1, y1, x2 - x1, y2 - y1);
            }

            return Empty;
        }

        public static Rectangle Union(Rectangle a, Rectangle b)
        {
            var x1 = Math.Min(a.X, b.X);
            var x2 = Math.Max(a.X + a.Width, b.X + b.Width);
            var y1 = Math.Min(a.Y, b.Y);
            var y2 = Math.Max(a.Y + a.Height, b.Y + b.Height);

            return new Rectangle(x1, y1, x2 - x1, y2 - y1);
        }

        public bool Equals(Rectangle other)
        {
            return X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        }

        public override bool Equals(object? obj)
        {
            return obj is Rectangle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(X, Y, Width, Height);
        }

        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue(nameof(X), X);
            info.AddValue(nameof(Y), Y);
            info.AddValue(nameof(Width), Width);
            info.AddValue(nameof(Height), Height);
        }

        public static Rectangle operator +(Rectangle rect, Point point)
        {
            return new(rect.X + point.X, rect.Y + point.Y, rect.Width, rect.Height);
        }

        public static Rectangle operator -(Rectangle rect, Point point)
        {
            return new(rect.X - point.X, rect.Y - point.Y, rect.Width, rect.Height);
        }

        public static Rectangle operator +(Rectangle rect, Size size)
        {
            return new(rect.X, rect.Y, rect.Width + size.Width, rect.Height + size.Height);
        }

        public static Rectangle operator -(Rectangle rect, Size size)
        {
            return new(rect.X, rect.Y, rect.Width - size.Width, rect.Height - size.Height);
        }

        public static bool operator ==(Rectangle left, Rectangle right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Rectangle left, Rectangle right)
        {
            return !left.Equals(right);
        }
    }
}