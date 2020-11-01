using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Serialization;
using System.Text;

namespace BEditor.Core.Media {
#nullable enable
    /// <summary>
    /// RectangleのサイズとPointを格納する構造体
    /// <para>System.Drawing.Rectangleのパクリ</para>
    /// </summary>
    [DataContract(Namespace = "")]
    public struct Rectangle : IEquatable<Rectangle> {
        /// <summary>
        /// 
        /// </summary>
        public static readonly Rectangle Empty;


        #region コンストラクタ

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="x">右上のX座標</param>
        /// <param name="y">右上のY座標</param>
        /// <param name="width">横幅</param>
        /// <param name="height">高さ</param>
        public Rectangle(in int x, in int y, in int width, in int height) {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="point">右上のpoint</param>
        /// <param name="size">サイズ</param>
        public Rectangle(Point2 point, Size size) {
            X = (int)point.X;
            Y = (int)point.Y;
            Width = size.Width;
            Height = size.Height;
        }

        #endregion


        #region Properties

        /// <summary>
        /// Rectangleの右上のX座標
        /// </summary>
        [DataMember(Order = 0)]
        public int X { get; set; }
        /// <summary>
        /// Rectangleの右上のY座標
        /// </summary>
        [DataMember(Order = 1)]
        public int Y { get; set; }
        /// <summary>
        /// Rectangleの横幅
        /// </summary>
        [DataMember(Order = 2)]
        public int Width { get; set; }
        /// <summary>
        /// Rectangleの高さ
        /// </summary>
        [DataMember(Order = 3)]
        public int Height { get; set; }

        /// <summary>
        /// 上端のY座標
        /// </summary>
        public int Top { get => Y; set => Y = value; }
        /// <summary>
        /// 下端のyY座標
        /// </summary>
        public int Bottom => Y + Height;
        /// <summary>
        /// 左端のX座標
        /// </summary>
        public int Left { get => X; set => X = value; }
        /// <summary>
        /// 右端のX座標
        /// </summary>
        public int Right => X + Width;
        /// <summary>
        /// 左上の頂点
        /// </summary>
        public Point2 TopLeft => new Point2(X, Y);
        /// <summary>
        /// 右下の頂点
        /// </summary>
        public Point2 BottomRight => new Point2(X + Width, Y + Height);


        /// <summary>
        /// Rectangleの左上のPoint
        /// </summary>
        public Point2 Point {
            get => new Point2(X, Y);
            set {
                X = (int)value.X;
                Y = (int)value.Y;
            }
        }
        /// <summary>
        /// Rectangleのサイズ
        /// </summary>
        public Size Size {
            get => new Size(Width, Height);
            set {
                Width = value.Width;
                Height = value.Height;
            }
        }

        #endregion

        #region StaticInit

        /// <summary>
        /// 指定した辺の位置を持つRectangleを作成します。
        /// </summary>
        /// <param name="left">左上のx座標</param>
        /// <param name="top">左上のy座標</param>
        /// <param name="right">右下のx座標</param>
        /// <param name="bottom">右下のy座標</param>
        /// <returns>作られたRectangle</returns>
        public static Rectangle FromLTRB(in int left, in int top, in int right, in int bottom) {
            var r = new Rectangle(
                x: left,
                y: top,
                width: right - left,
                height: bottom - top);

            if (r.Width < 0)
                throw new ArgumentException("right > left");
            if (r.Height < 0)
                throw new ArgumentException("bottom > top");
            return r;
        }
        /// <summary>
        /// 指定の量だけ膨張します
        /// </summary>
        /// <param name="rect">対象のRectangle</param>
        /// <param name="x">水平方向に膨張量</param>
        /// <param name="y">垂直方向に膨張量</param>
        public static Rectangle Inflate(Rectangle rect, in int x, in int y) {
            rect.Inflate(x, y);
            return rect;
        }
        /// <summary>
        /// 2つのRectangleの交差部分を表すRectangleを取得します
        /// </summary>
        public static Rectangle Intersect(Rectangle a, Rectangle b) {
            var x1 = Math.Max(a.X, b.X);
            var x2 = Math.Min(a.X + a.Width, b.X + b.Width);
            var y1 = Math.Max(a.Y, b.Y);
            var y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

            if (x2 >= x1 && y2 >= y1)
                return new Rectangle(x1, y1, x2 - x1, y2 - y1);
            return Empty;
        }
        /// <summary>
        /// 2つのRectangleの和集合を表す矩形を取得します
        /// </summary>
        public static Rectangle Union(Rectangle a, Rectangle b) {
            var x1 = Math.Min(a.X, b.X);
            var x2 = Math.Max(a.X + a.Width, b.X + b.Width);
            var y1 = Math.Min(a.Y, b.Y);
            var y2 = Math.Max(a.Y + a.Height, b.Y + b.Height);

            return new Rectangle(x1, y1, x2 - x1, y2 - y1);
        }

        #endregion


        #region Methhods

        /// <summary>
        /// 指定の量だけ膨張します
        /// </summary>
        /// <param name="width">水平方向に膨張量</param>
        /// <param name="height">垂直方向に膨張量</param>
        public void Inflate(in int width, in int height) {
            X -= width;
            Y -= height;
            Width += (2 * width);
            Height += (2 * height);
        }
        /// <summary>
        /// 指定の量だけ膨張します
        /// </summary>
        /// <param name="size">膨張量</param>
        public void Inflate(Size size) => Inflate(size.Width, size.Height);
        /// <summary>
        /// 2つのRectangleの交差部分を表すRectangleを取得します
        /// </summary>
        public Rectangle Intersect(Rectangle rect) => Intersect(this, rect);
        /// <summary>
        /// 指定したRectangleがこのRectangleと交差するかどうか
        /// </summary>
        public bool IntersectsWith(Rectangle rect) => (X < rect.X + rect.Width) &&
                                                      (X + Width > rect.X) &&
                                                      (Y < rect.Y + rect.Height) &&
                                                      (Y + Height > rect.Y);
        /// <summary>
        /// 2つのRectangleの和集合を表す矩形を取得します
        /// </summary>
        public Rectangle Union(Rectangle rect) => Union(this, rect);

        /// <summary>
        /// 指定したPointがこのRectangleに含まれているかどうかを判断する
        /// </summary>
        public bool Contains(in int x, in int y) => (X <= x && Y <= y && X + Width > x && Y + Height > y);
        /// <summary>
        /// 指定したPointがこのRectangleに含まれているかどうかを判断する
        /// </summary>
        public bool Contains(Point2 point) => Contains((int)point.X, (int)point.Y);
        /// <summary>
        /// 指定したRectangleがこのRectangleに含まれているかどうかを判断する
        /// </summary>
        public bool Contains(Rectangle rect) => X <= rect.X &&
                                                Y <= rect.Y &&
                                                (rect.X + rect.Width) <= (X + Width) &&
                                                (rect.Y + rect.Height) <= (Y + Height);

        #endregion


        /// <inheritdoc/>
        public bool Equals(Rectangle other) => X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;
        /// <inheritdoc/>
        public override bool Equals(object? obj) => obj is Rectangle other && Equals(other);
        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
        /// <inheritdoc/>
        public override string ToString() => $"(X:{X} Y:{Y} Width:{Width} Height:{Height})";


        #region 演算子


        /// <summary>
        /// 2つのRectangleの位置とサイズが等しいか
        /// </summary>
        public static bool operator ==(Rectangle left, Rectangle right) => left.Equals(right);
        /// <summary>
        /// 2つのRectangleの位置とサイズが等しくないか
        /// </summary>
        public static bool operator !=(Rectangle left, Rectangle right) => !left.Equals(right);

        /// <summary>
        /// Rectangleを移動させる
        /// </summary>
        public static Rectangle operator +(Rectangle rect, Point2 point) => new Rectangle((int)(rect.X + point.X), (int)(rect.Y + point.Y), rect.Width, rect.Height);
        /// <summary>
        /// Rectangleを移動させる
        /// </summary>
        public static Rectangle operator -(Rectangle rect, Point2 point) => new Rectangle((int)(rect.X - point.X), (int)(rect.Y - point.Y), rect.Width, rect.Height);
        /// <summary>
        /// Rectangleを膨張する
        /// </summary>
        public static Rectangle operator +(Rectangle rect, Size size) => new Rectangle(rect.X, rect.Y, rect.Width + size.Width, rect.Height + size.Height);
        /// <summary>
        /// Rectangleを縮小する
        /// </summary>
        public static Rectangle operator -(Rectangle rect, Size size) => new Rectangle(rect.X, rect.Y, rect.Width - size.Width, rect.Height - size.Height);

        #endregion

        /// <summary>
        /// 
        /// </summary>
        /// <param name="rect"></param>
        public static explicit operator System.Drawing.Rectangle(Rectangle rect) => new System.Drawing.Rectangle(rect.X, rect.Y, rect.Width, rect.Height);
        /// <summary>
        /// 
        /// </summary>
        /// <param name="rect"></param>
        public static explicit operator Rectangle(System.Drawing.Rectangle rect) => new Rectangle(rect.X, rect.Y, rect.Width, rect.Height);
    }
}
