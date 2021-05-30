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
        /// Generates an image with an polygon drawn on it.
        /// </summary>
        /// <param name="number">The number of corners of the polygon.</param>
        /// <param name="width">The width of the polygon.</param>
        /// <param name="height">The height of the polygon.</param>
        /// <param name="color">The color of the polygon.</param>
        /// <returns>Returns an image with an polygon drawn on it.</returns>
        public static Image<BGRA32> Polygon(int number, int width, int height, Color color)
        {
            var radiusX = width / 2;
            var radiusY = height / 2;
            var points = GetPolygonVertex(number, new(radiusX, radiusY), radiusX, radiusY, 0.5);

            using var bmp = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bmp);

            using var path = new SKPath();
            path.MoveTo(points[0].X, points[0].Y);

            foreach (var p in points)
            {
                path.LineTo(p.X, p.Y);
            }

            path.Close();

            using var paint = new SKPaint
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
            };

            canvas.DrawPath(path, paint);

            return bmp.ToImage32();
        }

        /// <summary>
        /// Generates an image with an text drawn on it.
        /// </summary>
        /// <param name="text">The string to draw.</param>
        /// <param name="font">The font of the string to be drawn.</param>
        /// <param name="size">The size of the text.</param>
        /// <param name="color">The color of the text.</param>
        /// <param name="hAlign">The horizontal alignment of the text.</param>
        /// <param name="vAlign">The vertical alignment of the text.</param>
        /// <param name="linespace">The line spacing for the text.</param>
        /// <returns>Returns an image with an text drawn on it.</returns>
        public static Image<BGRA32> Text(
            string text,
            Font font,
            float size,
            Color color,
            HorizontalAlign hAlign,
            VerticalAlign vAlign,
            float linespace = 0)
        {
            if (string.IsNullOrEmpty(text)) return new Image<BGRA32>(1, 1, default(BGRA32));
            if (font is null) throw new ArgumentNullException(nameof(font));

            return TextHorizontal(text, font, size, color, hAlign, linespace);
        }

        /// <summary>
        /// Generates an image with an text drawn on it.
        /// </summary>
        /// <param name="text">The string to draw.</param>
        /// <param name="font">The font of the string to be drawn.</param>
        /// <param name="size">The size of the text.</param>
        /// <param name="strokewidth">The stroke width of the stroke text.</param>
        /// <param name="color">The color of the text.</param>
        /// <param name="hAlign">The horizontal alignment of the text.</param>
        /// <param name="linespace">The line spacing for the text.</param>
        /// <returns>Returns an image with an text drawn on it.</returns>
        public static Image<BGRA32> StrokeText(string text, Font font, float size, float strokewidth, Color color, HorizontalAlign hAlign, float linespace = 0)
        {
            if (string.IsNullOrEmpty(text)) return new Image<BGRA32>(1, 1, default(BGRA32));
            if (font is null) throw new ArgumentNullException(nameof(font));
            var lines = text.Replace("\r\n", "\n").Split('\n');

            var face = font.GetTypeface();
            using var fontObj = new SKFont(face, size)
            {
                Edging = SKFontEdging.Antialias,
            };

            using var paint = new SKPaint(fontObj)
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = strokewidth,
            };

            using var linesBounds = new UnmanagedList<SKRect>();

            for (var i = 0; i < lines.Length; i++)
            {
                var item = lines[i];
                var textBounds = default(SKRect);

                paint.MeasureText(item, ref textBounds);
                linesBounds.Add(textBounds);
            }

            using var bmp = new SKBitmap(new SKImageInfo((int)linesBounds.Max(i => i.Width), (int)(linesBounds.Sum(i => i.Height) + (linespace * (lines.Length - 1))), SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);

            for (var i = 0; i < lines.Length; i++)
            {
                var txt = lines[i];
                var bounds = linesBounds[i];
                using var path = paint.GetTextPath(txt, (bounds.Width / 2) - bounds.MidX, (bounds.Height / 2) - bounds.MidY);

                if (hAlign is HorizontalAlign.Right)
                {
                    var x = bmp.Width - bounds.Width;
                    canvas.Translate(x, 0);
                    canvas.DrawPath(path, paint);

                    canvas.Translate(-x, bounds.Height + linespace);
                }
                else if (hAlign is HorizontalAlign.Left)
                {
                    canvas.DrawPath(path, paint);

                    canvas.Translate(0, bounds.Height + linespace);
                }
                else
                {
                    var x = (bmp.Width - bounds.Width) / 2;
                    canvas.Translate(x, 0);
                    canvas.DrawPath(path, paint);

                    canvas.Translate(-x, bounds.Height + linespace);
                }
            }

            canvas.Flush();

            return bmp.ToImage32();
        }

        private static Image<BGRA32> TextHorizontal(string text, Font font, float size, Color color, HorizontalAlign hAlign, float linespace = 0)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');

            var face = font.GetTypeface();
            using var fontObj = new SKFont(face, size)
            {
                Edging = SKFontEdging.Antialias,
            };

            using var paint = new SKPaint(fontObj)
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = true,
            };

            using var linesBounds = new UnmanagedList<SKRect>();

            for (var i = 0; i < lines.Length; i++)
            {
                var item = lines[i];
                var textBounds = default(SKRect);

                paint.MeasureText(item, ref textBounds);
                linesBounds.Add(textBounds);
            }

            using var bmp = new SKBitmap(new SKImageInfo((int)linesBounds.Max(i => i.Width), (int)(linesBounds.Sum(i => i.Height) + (linespace * (lines.Length - 1))), SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);

            for (var i = 0; i < lines.Length; i++)
            {
                var txt = lines[i];
                var bounds = linesBounds[i];
                using var path = paint.GetTextPath(txt, (bounds.Width / 2) - bounds.MidX, (bounds.Height / 2) - bounds.MidY);

                if (hAlign is HorizontalAlign.Right)
                {
                    var x = bmp.Width - bounds.Width;
                    canvas.Translate(x, 0);
                    canvas.DrawPath(path, paint);

                    canvas.Translate(-x, bounds.Height + linespace);
                }
                else if (hAlign is HorizontalAlign.Left)
                {
                    canvas.DrawPath(path, paint);

                    canvas.Translate(0, bounds.Height + linespace);
                }
                else
                {
                    var x = (bmp.Width - bounds.Width) / 2;
                    canvas.Translate(x, 0);
                    canvas.DrawPath(path, paint);

                    canvas.Translate(-x, bounds.Height + linespace);
                }
            }

            canvas.Flush();

            return bmp.ToImage32();
        }

        private static Point[] GetPolygonVertex(int number, Point center, double radiusX, double radiusY, double rotate)
        {
            try
            {
                if (number <= 2)
                    throw new ArgumentException(null, nameof(number));

                var vertexes = new Point[number];
                for (var pos = 0; pos < number; pos++)
                {
                    vertexes[pos] = new Point(
                        (int)(Math.Sin(((pos + rotate) * (2 * Math.PI)) / number) * radiusX) + center.X,
                        (int)(Math.Cos(((pos + rotate) * (2 * Math.PI)) / number) * radiusY) + center.Y);
                }

                return vertexes;
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}