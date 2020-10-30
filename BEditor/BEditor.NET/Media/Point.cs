using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;
using System.Text;
#if OldOpenTK
using OpenTK;
#else
using OpenTK.Mathematics;
#endif

#nullable enable

namespace BEditor.NET.Media {
    /// <summary>
    /// 
    /// </summary>
    [DataContract(Namespace = "")]
    public struct Point2 : IEquatable<Point2> {
        /// <summary>
        /// 
        /// </summary>
        public static readonly Point2 Empty;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public Point2(int x, int y) {
            X = x;
            Y = y;
        }

        public Point2(float x, float y) {
            X = x;
            Y = y;
        }

        public Point2(double x, double y) {
            X = (float)x;
            Y = (float)y;
        }

        /// <summary>
        /// 
        /// </summary>
        [DataMember(Order = 0)]
        public float X { get; set; }
        /// <summary>
        /// 
        /// </summary>
        [DataMember(Order = 1)]
        public float Y { get; set; }


        #region StaticMethods

        /// <summary>
        /// 
        /// </summary>
        /// <param name="point"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static Point2 Add(Point2 point, Size size) => new Point2() {
            X = point.X + size.Width,
            Y = point.Y + size.Height
        };
        /// <summary>
        /// 
        /// </summary>
        /// <param name="point1"></param>
        /// <param name="point2"></param>
        /// <returns></returns>
        public static Point2 Add(Point2 point1, Point2 point2) => new Point2() {
            X = point1.X + point2.X,
            Y = point1.Y + point2.Y
        };
        /// <summary>
        /// 
        /// </summary>
        /// <param name="point"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static Point2 Subtract(Point2 point, Size size) => new Point2() {
            X = point.X - size.Width,
            Y = point.Y - size.Height
        };
        /// <summary>
        /// 
        /// </summary>
        /// <param name="point1"></param>
        /// <param name="point2"></param>
        /// <returns></returns>
        public static Point2 Subtract(Point2 point1, Point2 point2) => new Point2() {
            X = point1.X - point2.X,
            Y = point1.Y - point2.Y
        };

        #endregion

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Point2 point && Equals(point);
        /// <inheritdoc/>
        public bool Equals(Point2 other) => X == other.X && Y == other.Y;
        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(X, Y);
        /// <inheritdoc/>
        public override string ToString() => $"(X:{X} Y:{Y})";

        #region 演算子

        /// <summary>
        /// 
        /// </summary>
        /// <param name="point1"></param>
        /// <param name="point2"></param>
        /// <returns></returns>
        public static Point2 operator +(Point2 point1, Point2 point2) => Add(point1, point2);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="point"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static Point2 operator +(Point2 point, Size size) => Add(point, size);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="point1"></param>
        /// <param name="point2"></param>
        /// <returns></returns>
        public static Point2 operator -(Point2 point1, Point2 point2) => Subtract(point1, point2);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="point"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static Point2 operator -(Point2 point, Size size) => Subtract(point, size);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator ==(Point2 left, Point2 right) => left.Equals(right);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator !=(Point2 left, Point2 right) => !left.Equals(right);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="point"></param>
        public static explicit operator System.Drawing.Point(Point2 point) => new System.Drawing.Point((int)point.X, (int)point.Y);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="point"></param>
        public static explicit operator Point2(System.Drawing.Point point) => new Point2(point.X, point.Y);
        
        #endregion
    }

    /// <summary>
    /// 
    /// </summary>
    [DataContract(Namespace = "")]
    public struct Point3 : IEquatable<Point3> {
        /// <summary>
        /// 
        /// </summary>
        public static readonly Point3 Empty;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        public Point3(int x, int y, int z) {
            X = x;
            Y = y;
            Z = z;
        }

        public Point3(float x, float y, float z) {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>
        /// 
        /// </summary>
        [DataMember(Order = 0)]
        public float X { get; set; }
        /// <summary>
        /// 
        /// </summary>
        [DataMember(Order = 1)]
        public float Y { get; set; }
        /// <summary>
        /// 
        /// </summary>
        [DataMember(Order = 2)]
        public float Z { get; set; }


        #region StaticMethods

        /// <summary>
        /// 
        /// </summary>
        /// <param name="point"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static Point3 Add(Point3 point, Size size) => new Point3() {
            X = point.X + size.Width,
            Y = point.Y + size.Height,
            Z = point.Z
        };
        /// <summary>
        /// 
        /// </summary>
        /// <param name="point1"></param>
        /// <param name="point2"></param>
        /// <returns></returns>
        public static Point3 Add(Point3 point1, Point3 point2) => new Point3() {
            X = point1.X + point2.X,
            Y = point1.Y + point2.Y,
            Z = point1.Z + point2.Z
        };
        /// <summary>
        /// 
        /// </summary>
        /// <param name="point"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static Point3 Subtract(Point3 point, Size size) => new Point3() {
            X = point.X - size.Width,
            Y = point.Y - size.Height,
            Z = point.Z
        };
        /// <summary>
        /// 
        /// </summary>
        /// <param name="point1"></param>
        /// <param name="point2"></param>
        /// <returns></returns>
        public static Point3 Subtract(Point3 point1, Point3 point2) => new Point3() {
            X = point1.X - point2.X,
            Y = point1.Y - point2.Y,
            Z = point1.Z - point2.Z
        };

        #endregion

        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Point3 point && Equals(point);
        /// <inheritdoc/>
        public bool Equals(Point3 other) => X == other.X && Y == other.Y;
        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        /// <inheritdoc/>
        public override string ToString() => $"(X:{X} Y:{Y} Z:{Z})";

        #region 演算子

        /// <summary>
        /// 
        /// </summary>
        /// <param name="point1"></param>
        /// <param name="point2"></param>
        /// <returns></returns>
        public static Point3 operator +(Point3 point1, Point3 point2) => Add(point1, point2);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="point"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static Point3 operator +(Point3 point, Size size) => Add(point, size);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="point1"></param>
        /// <param name="point2"></param>
        /// <returns></returns>
        public static Point3 operator -(Point3 point1, Point3 point2) => Subtract(point1, point2);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="point"></param>
        /// <param name="size"></param>
        /// <returns></returns>
        public static Point3 operator -(Point3 point, Size size) => Subtract(point, size);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator ==(Point3 left, Point3 right) => left.Equals(right);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="left"></param>
        /// <param name="right"></param>
        /// <returns></returns>
        public static bool operator !=(Point3 left, Point3 right) => !left.Equals(right);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="point"></param>
        public static explicit operator System.Drawing.Point(Point3 point) => new System.Drawing.Point((int)point.X, (int)point.Y);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="point"></param>
        public static explicit operator Point3(System.Drawing.Point point) => new Point3(point.X, point.Y, 0);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="point"></param>
        public static explicit operator Vector3(Point3 point) => new Vector3(point.X, point.Y, point.Z);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="point"></param>
        public static explicit operator Point3(Vector3 point) => new Point3(point.X, point.Y, point.Z);
        
        #endregion
    }
}
