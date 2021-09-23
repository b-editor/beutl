// SkiaObjects.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;

using SkiaSharp;

namespace BEditor.Drawing
{
    /// <inheritdoc cref="Image"/>
    public static partial class Image
    {
        /// <summary>
        /// Generates an image with an ellipse drawn on it.
        /// </summary>
        /// <param name="width">The width of the ellipse.</param>
        /// <param name="height">The height of the ellipse.</param>
        /// <param name="line">The line width of the ellipse.</param>
        /// <param name="color">The color of the ellipse.</param>
        /// <returns>Returns an image with an ellipse drawn on it.</returns>
        public static Image<BGRA32> Ellipse(int width, int height, int line, Color color)
        {
            return Ellipse(width, height, new()
            {
                StrokeWidth = line,
                Color = color,
                Style = BrushStyle.Stroke,
                IsAntialias = true,
            });
        }

        /// <summary>
        /// Generates an image with an ellipse drawn on it.
        /// </summary>
        /// <param name="width">The width of the ellipse.</param>
        /// <param name="height">The height of the ellipse.</param>
        /// <param name="brush">A brush used to draw an ellipse.</param>
        /// <returns>Returns an image with an ellipse drawn on it.</returns>
        public static Image<BGRA32> Ellipse(int width, int height, Brush brush)
        {
            var line = brush.StrokeWidth;
            var color = brush.Color;

            if (line >= Math.Min(width, height) / 2)
                line = Math.Min(width, height) / 2;

            var min = Math.Min(width, height);

            if (line < min) min = line;
            if (min < 0) min = 0;

            using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);

            using var paint = new SKPaint
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = brush.IsAntialias,
                Style = (SKPaintStyle)brush.Style,
                StrokeWidth = min,
            };

            canvas.DrawOval(
                new SKPoint(width / 2, height / 2),
                new SKSize((width / 2) - (min / 2), (height / 2) - (min / 2)),
                paint);

            return bmp.ToImage32();
        }

        /// <summary>
        /// Generates an image with an ellipse drawn on it.
        /// </summary>
        /// <param name="size">The size of the ellipse.</param>
        /// <param name="brush">A brush used to draw an ellipse.</param>
        /// <returns>Returns an image with an ellipse drawn on it.</returns>
        public static Image<BGRA32> Ellipse(Size size, Brush brush)
        {
            return Ellipse(size.Width, size.Height, brush);
        }

        /// <summary>
        /// Generates an image with an rectangle drawn on it.
        /// </summary>
        /// <param name="width">The width of the rectangle.</param>
        /// <param name="height">The height of the rectangle.</param>
        /// <param name="line">The line width of the rectangle.</param>
        /// <param name="color">The color of the rectangle.</param>
        /// <returns>Returns an image with an rectangle drawn on it.</returns>
        public static Image<BGRA32> Rect(int width, int height, int line, Color color)
        {
            return Rect(width, height, new()
            {
                StrokeWidth = line,
                Style = BrushStyle.Stroke,
                IsAntialias = true,
                Color = color,
            });
        }

        /// <summary>
        /// Generates an image with an rectangle drawn on it.
        /// </summary>
        /// <param name="width">The width of the rectangle.</param>
        /// <param name="height">The height of the rectangle.</param>
        /// <param name="brush">A brush used to draw an rectangle.</param>
        /// <returns>Returns an image with an rectangle drawn on it.</returns>
        public static Image<BGRA32> Rect(int width, int height, Brush brush)
        {
            var color = brush.Color;
            var line = brush.StrokeWidth * 2;
            using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);

            using var paint = new SKPaint
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = brush.IsAntialias,
                Style = (SKPaintStyle)brush.Style,
                StrokeWidth = line,
            };

            canvas.DrawRect(
                0, 0,
                width, height,
                paint);

            return bmp.ToImage32();
        }

        /// <summary>
        /// Generates an image with an rectangle drawn on it.
        /// </summary>
        /// <param name="size">The size of the rectangle.</param>
        /// <param name="brush">A brush used to draw an rectangle.</param>
        /// <returns>Returns an image with an rectangle drawn on it.</returns>
        public static Image<BGRA32> Rect(Size size, Brush brush)
        {
            return Rect(size.Width, size.Height, brush);
        }

        /// <summary>
        /// Generates an image with an arc drawn on it.
        /// </summary>
        /// <param name="width">The width of the arc.</param>
        /// <param name="height">The height of the arc.</param>
        /// <param name="startAngle">The start angle.</param>
        /// <param name="sweepAngle">The sweep angle.</param>
        /// <param name="useCenter">Use center.</param>
        /// <param name="brush">A brush used to draw an arc.</param>
        /// <returns>Returns an image with an arc drawn on it.</returns>
        public static Image<BGRA32> Arc(int width, int height, float startAngle, float sweepAngle, bool useCenter, Brush brush)
        {
            var line = brush.StrokeWidth;
            var color = brush.Color;

            if (line >= Math.Min(width, height) / 2)
                line = Math.Min(width, height) / 2;

            var min = Math.Min(width, height);

            if (line < min) min = line;
            if (min < 0) min = 0;

            using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);

            using var paint = new SKPaint
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = brush.IsAntialias,
                Style = (SKPaintStyle)brush.Style,
                StrokeWidth = min,
            };

            var w = width - min;
            var h = height - min;
            canvas.DrawArc(SKRect.Create(min / 2, min / 2, w, h), startAngle, sweepAngle, useCenter, paint);

            return bmp.ToImage32();
        }

        /// <summary>
        /// Generates an image with an polygon drawn on it.
        /// </summary>
        /// <param name="number">The number of corners of the polygon.</param>
        /// <param name="width">The width of the polygon.</param>
        /// <param name="height">The height of the polygon.</param>
        /// <param name="line">The line width of the polygon.</param>
        /// <param name="color">The color of the polygon.</param>
        /// <returns>Returns an image with an polygon drawn on it.</returns>
        public static Image<BGRA32> Polygon(int number, int width, int height, int line, Color color)
        {
            var radiusX = width / 2;
            var radiusY = height / 2;
            using var path = GetPolygonVertex(number, new(radiusX, radiusY), radiusX, radiusY, 0.5, line);

            using var bmp = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bmp);

            using var paint = new SKPaint
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };

            canvas.DrawPath(path, paint);

            return bmp.ToImage32();
        }

        private static SKPath GetPolygonVertex(int number, Point center, double radiusX, double radiusY, double rotate, double line)
        {
            try
            {
                if (number <= 2)
                    throw new ArgumentException(null, nameof(number));

                if (line >= Math.Min(radiusX, radiusY))
                    line = Math.Min(radiusX, radiusY);

                var min = Math.Min(radiusX, radiusY) * 2;

                if (line < min) min = line;
                if (min < 0) min = 0;

                var path = new SKPath();

                for (var pos = 0; pos < number; pos++)
                {
                    var next = pos + 1;
                    if (next > number) next = 0;

                    path.MoveTo(
                        (int)(Math.Sin(((pos + rotate) * (2 * Math.PI)) / number) * radiusX) + center.X,
                        (int)(Math.Cos(((pos + rotate) * (2 * Math.PI)) / number) * radiusY) + center.Y);

                    path.LineTo(
                        (int)(Math.Sin(((pos + rotate) * (2 * Math.PI)) / number) * radiusX) + center.X,
                        (int)(Math.Cos(((pos + rotate) * (2 * Math.PI)) / number) * radiusY) + center.Y);

                    path.LineTo(
                        (int)(Math.Sin(((next + rotate) * (2 * Math.PI)) / number) * radiusX) + center.X,
                        (int)(Math.Cos(((next + rotate) * (2 * Math.PI)) / number) * radiusY) + center.Y);

                    path.LineTo(
                        (int)(Math.Sin(((next + rotate) * (2 * Math.PI)) / number) * (radiusX - min)) + center.X,
                        (int)(Math.Cos(((next + rotate) * (2 * Math.PI)) / number) * (radiusY - min)) + center.Y);

                    path.LineTo(
                        (int)(Math.Sin(((pos + rotate) * (2 * Math.PI)) / number) * (radiusX - min)) + center.X,
                        (int)(Math.Cos(((pos + rotate) * (2 * Math.PI)) / number) * (radiusY - min)) + center.Y);

                    path.Close();
                }

                return path;
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}