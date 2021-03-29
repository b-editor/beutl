using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.Process;
using BEditor.Drawing.Properties;

using SkiaSharp;

namespace BEditor.Drawing
{
    public unsafe static partial class Image
    {
        public static void DrawImage<T>(this Image<T> self, Point point, Image<T> image) where T : unmanaged, IPixel<T>
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (image is null) throw new ArgumentNullException(nameof(image));
            self.ThrowIfDisposed();
            image.ThrowIfDisposed();
            var rect = new Rectangle(point, image.Size);
            var blended = self[rect];

            blended.Blend(image, blended);

            self[rect] = blended;
        }
        public static void DrawImage(this Image<BGRA32> self, Point point, Image<BGRA32> image)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (image is null) throw new ArgumentNullException(nameof(image));
            self.ThrowIfDisposed();
            image.ThrowIfDisposed();

            using var paint = new SKPaint() { IsAntialias = true };
            using var bmp = self.ToSKBitmap();
            using var canvas = new SKCanvas(bmp);
            using var b = image.ToSKBitmap();

            canvas.DrawBitmap(b, point.X, point.Y, paint);

            CopyTo(bmp.Bytes, self.Data, self.DataSize);
        }
        public static void DrawPath(this Image<BGRA32> self, BGRA32 color, Point point, Point[] points)
        {
            using var bmp = self.ToSKBitmap();
            using var canvas = new SKCanvas(bmp);

            using var path = new SKPath();
            path.MoveTo(point.X, point.Y);

            foreach (var p in points)
            {
                path.LineTo(p.X, p.Y);
            }

            path.Close();

            using var paint = new SKPaint()
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            canvas.DrawPath(path, paint);

            CopyTo(bmp.Bytes, self.Data, self.DataSize);
        }
        internal static Image<BGR24> ToImage24(this SKBitmap self)
        {
            var result = new Image<BGR24>(self.Width, self.Height);
            CopyTo(self.Bytes, result.Data, result.DataSize);

            return result;
        }
        internal static Image<BGRA32> ToImage32(this SKBitmap self)
        {
            var result = new Image<BGRA32>(self.Width, self.Height);
            CopyTo(self.Bytes, result.Data!, result.DataSize);

            return result;
        }
        internal static SKBitmap ToSKBitmap(this Image<BGR24> self)
        {
            var result = new SKBitmap(new(self.Width, self.Height, SKColorType.Rgb888x));

            fixed (BGR24* src = self.Data)
                result.SetPixels((IntPtr)src);

            return result;
        }
        internal static SKBitmap ToSKBitmap(this Image<BGRA32> self)
        {
            var result = new SKBitmap(new(self.Width, self.Height, SKColorType.Bgra8888));

            fixed (BGRA32* src = self.Data)
                result.SetPixels((IntPtr)src);

            return result;
        }
        internal static void CopyTo(byte[] src, Span<BGR24> dst, int length)
        {
            fixed (BGR24* dstPtr = dst)
            fixed (byte* srcPtr = src)
            {
                Buffer.MemoryCopy(srcPtr, dstPtr, length, length);
            }
        }
        internal static void CopyTo(byte[] src, Span<BGRA32> dst, int length)
        {
            fixed (BGRA32* dstPtr = dst)
            fixed (byte* srcPtr = src)
            {
                Buffer.MemoryCopy(srcPtr, dstPtr, length, length);
            }
        }

        public static void SetAlpha(this Image<BGRA32> self, float alpha)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            self.ThrowIfDisposed();

            fixed (BGRA32* data = self.Data)
            {
                var p = new SetAlphaProcess(data, alpha);
                Parallel.For(0, self.Data.Length, p.Invoke);
            }
        }
        public static void SetColor(this Image<BGRA32> self, BGRA32 color)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            self.ThrowIfDisposed();

            fixed (BGRA32* data = self.Data)
            {
                var p = new SetColorProcess(data, color);
                Parallel.For(0, self.Data.Length, p.Invoke);
            }
        }
        public static Image<BGRA32> Border(this Image<BGRA32> self, int size, BGRA32 color)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (size <= 0) throw new ArgumentException(string.Format(Resources.LessThan, nameof(size), 0));
            self.ThrowIfDisposed();

            int nwidth = self.Width + (size + 5) * 2;
            int nheight = self.Height + (size + 5) * 2;

            using var filter = SKImageFilter.CreateDilate(size, size);
            using var dilatePaint = new SKPaint { ImageFilter = filter, IsAntialias = true };
            using var paint = new SKPaint() { IsAntialias = true };
            using var bmp = new SKBitmap(new(nwidth, nheight, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);
            using var b = self.Clone();
            b.SetColor(color);
            using var b_ = b.ToSKBitmap();
            using var s = self.ToSKBitmap();

            var x = (nwidth - self.Width) / 2;
            var y = (nheight - self.Height) / 2;

            canvas.DrawBitmap(b_, x, y, dilatePaint);
            canvas.DrawBitmap(s, x, y, paint);

            return bmp.ToImage32();
        }
        public static Image<BGRA32> Shadow(this Image<BGRA32> self, float x, float y, float blur, float alpha, BGRA32 color)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            self.ThrowIfDisposed();
            var w = self.Width + blur;
            var h = self.Height + blur;

            //キャンバスのサイズ
            var size_w = (Math.Abs(x) + (w / 2)) * 2;
            var size_h = (Math.Abs(y) + (h / 2)) * 2;

            using var filter = SKImageFilter.CreateDropShadow(x, y, blur, blur, new SKColor(color.R, color.G, color.B, (byte)(color.A * alpha)));
            using var paint = new SKPaint()
            {
                ImageFilter = filter,
                IsAntialias = true
            };

            using var bmp = new SKBitmap((int)size_w, (int)size_h);
            using var canvas = new SKCanvas(bmp);
            using var d = self.ToSKBitmap();

            canvas.DrawBitmap(
                d,
                (size_w / 2 - self.Width / 2),
                (size_h / 2 - self.Height / 2),
                paint);

            return bmp.ToImage32();
        }

        #region Blur
        public static void Blur(this Image<BGRA32> self, float sigma)
        {
            self.Blur(sigma, sigma);
        }
        public static void Blur(this Image<BGRA32> self, float sigmaX, float sigmaY)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            self.ThrowIfDisposed();

            using var filter = SKImageFilter.CreateBlur(sigmaX, sigmaY);
            using var paint = new SKPaint { ImageFilter = filter, IsAntialias = true };
            using var bmp = new SKBitmap(new(self.Width, self.Height, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);
            using var b = self.ToSKBitmap();

            canvas.DrawBitmap(b, 0, 0, paint);

            CopyTo(bmp.Bytes, self.Data!, self.DataSize);
        }
        public static void Blur(this Image<BGR24> self, float sigma)
        {
            self.Blur(sigma, sigma);
        }
        public static void Blur(this Image<BGR24> self, float sigmaX, float sigmaY)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            self.ThrowIfDisposed();

            using var filter = SKImageFilter.CreateBlur(sigmaX, sigmaY);
            using var paint = new SKPaint { ImageFilter = filter, IsAntialias = true };
            using var bmp = new SKBitmap(new(self.Width, self.Height, SKColorType.Rgb888x));
            using var canvas = new SKCanvas(bmp);
            using var b = self.ToSKBitmap();

            canvas.DrawBitmap(b, 0, 0, paint);

            CopyTo(bmp.Bytes, self.Data!, self.DataSize);
        }
        #endregion

        #region Dilate
        public static void Dilate(this Image<BGRA32> self, int radius)
        {
            self.Dilate(radius, radius);
        }
        public static void Dilate(this Image<BGRA32> self, int radiusX, int radiusY)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            self.ThrowIfDisposed();

            using var filter = SKImageFilter.CreateDilate(radiusX, radiusY);
            using var paint = new SKPaint { ImageFilter = filter, IsAntialias = true };
            using var bmp = new SKBitmap(new(self.Width, self.Height, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);
            using var b = self.ToSKBitmap();

            canvas.DrawBitmap(b, 0, 0, paint);

            CopyTo(bmp.Bytes, self.Data!, self.DataSize);
        }
        public static void Dilate(this Image<BGR24> self, int radius)
        {
            self.Dilate(radius, radius);
        }
        public static void Dilate(this Image<BGR24> self, int radiusX, int radiusY)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            self.ThrowIfDisposed();

            using var filter = SKImageFilter.CreateDilate(radiusX, radiusY);
            using var paint = new SKPaint { ImageFilter = filter, IsAntialias = true };
            using var bmp = new SKBitmap(new(self.Width, self.Height, SKColorType.Rgb888x));
            using var canvas = new SKCanvas(bmp);
            using var b = self.ToSKBitmap();

            canvas.DrawBitmap(b, 0, 0, paint);

            CopyTo(bmp.Bytes, self.Data!, self.DataSize);
        }
        #endregion

        #region Erode
        public static void Erode(this Image<BGRA32> self, int radius)
        {
            self.Erode(radius, radius);
        }
        public static void Erode(this Image<BGRA32> self, int radiusX, int radiusY)
        {
            self.ThrowIfDisposed();

            using var filter = SKImageFilter.CreateErode(radiusX, radiusY);
            using var paint = new SKPaint { ImageFilter = filter, IsAntialias = true };
            using var bmp = new SKBitmap(new(self.Width, self.Height, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);
            using var b = self.ToSKBitmap();

            canvas.DrawBitmap(b, 0, 0, paint);

            CopyTo(bmp.Bytes, self.Data!, self.DataSize);
        }
        public static void Erode(this Image<BGR24> self, int radius)
        {
            self.Erode(radius, radius);
        }
        public static void Erode(this Image<BGR24> self, int radiusX, int radiusY)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            self.ThrowIfDisposed();

            using var filter = SKImageFilter.CreateErode(radiusX, radiusY);
            using var paint = new SKPaint { ImageFilter = filter, IsAntialias = true };
            using var bmp = new SKBitmap(new(self.Width, self.Height, SKColorType.Rgb888x));
            using var canvas = new SKCanvas(bmp);
            using var b = self.ToSKBitmap();

            canvas.DrawBitmap(b, 0, 0, paint);

            CopyTo(bmp.Bytes, self.Data!, self.DataSize);
        }
        #endregion

        #region LinerGradient

        public static void LinerGradient(this Image<BGRA32> self, PointF start, PointF end, IEnumerable<Color> colors, IEnumerable<float> anchors, ShaderTileMode mode)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (colors is null) throw new ArgumentNullException(nameof(colors));
            if (anchors is null) throw new ArgumentNullException(nameof(anchors));
            self.ThrowIfDisposed();
            self.SetColor(new BGRA32(255, 255, 255, 255));

            var w = self.Width;
            var h = self.Height;
            var st = new SKPoint(start.X * w * 0.01f, start.Y * h * 0.01f);
            var ed = new SKPoint(end.X * w * 0.01f, end.Y * h * 0.01f);

            using var paint = new SKPaint()
            {
                BlendMode = SKBlendMode.Modulate,
                IsAntialias = true
            };
            using var bmp = self.ToSKBitmap();
            using var canvas = new SKCanvas(bmp);

            paint.Shader = SKShader.CreateLinearGradient(
                st,
                ed,
                colors.Select(c => new SKColor(c.R, c.G, c.B, c.A)).ToArray(),
                anchors.ToArray(),
                (SKShaderTileMode)mode);

            canvas.DrawRect(0, 0, self.Width, self.Height, paint);

            CopyTo(bmp.Bytes, self.Data!, self.DataSize);
        }

        #endregion

        #region CircularGradient

        public static void CircularGradient(this Image<BGRA32> self, PointF center, float radius, IEnumerable<Color> colors, IEnumerable<float> colorpos, ShaderTileMode mode)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (colors is null) throw new ArgumentNullException(nameof(colors));
            if (colorpos is null) throw new ArgumentNullException(nameof(colorpos));
            self.ThrowIfDisposed();
            self.SetColor(new BGRA32(255, 255, 255, 255));
            var pt = new SKPoint(center.X + self.Width / 2, center.Y + self.Height / 2);

            using var paint = new SKPaint()
            {
                BlendMode = SKBlendMode.Modulate,
                IsAntialias = true
            };
            using var bmp = self.ToSKBitmap();
            using var canvas = new SKCanvas(bmp);

            paint.Shader = SKShader.CreateRadialGradient(
                pt,
                radius,
                colors.Select(c => new SKColor(c.R, c.G, c.B, c.A)).ToArray(),
                colorpos.ToArray(),
                (SKShaderTileMode)mode);

            canvas.DrawRect(0, 0, self.Width, self.Height, paint);

            CopyTo(bmp.Bytes, self.Data!, self.DataSize);
        }

        #endregion

        #region Mask

        public static void Mask(this Image<BGRA32> self, Image<BGRA32> mask, PointF point, float rotate, bool invert)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (mask is null) throw new ArgumentNullException(nameof(mask));
            self.ThrowIfDisposed();
            mask.ThrowIfDisposed();
            mask.SetColor(default);

            using var paint = new SKPaint()
            {
                BlendMode = invert ? SKBlendMode.DstOut : SKBlendMode.DstIn,
                IsAntialias = true
            };
            using var bmp = self.ToSKBitmap();
            using var canvas = new SKCanvas(bmp);
            using var m = MakeMask(self.Size, mask, point, rotate);


            canvas.DrawBitmap(m, new SKPoint(), paint);

            CopyTo(bmp.Bytes, self.Data!, self.DataSize);
        }
        private static SKBitmap MakeMask(Size size, Image<BGRA32> mask, PointF point, float rotate)
        {
            using var paint = new SKPaint();
            var bmp = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);
            using var m = mask.ToSKBitmap();

            canvas.RotateDegrees(rotate);
            canvas.DrawBitmap(
                m,
                new SKPoint(
                    point.X + (size.Width - mask.Width) / 2F,
                    point.Y + (size.Height - mask.Height) / 2F),
                paint);

            return bmp;
        }

        #endregion

        #region LightDiffuse

        public static void PointLightDiffuse(this Image<BGRA32> self, Point3F location, Color lightColor, float surfaceScale, float kd)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            self.ThrowIfDisposed();

            using var bmp = new SKBitmap(new(self.Width, self.Height, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);
            using var b = self.ToSKBitmap();
            using var paint = new SKPaint
            {
                IsAntialias = true,
            };
            paint.ImageFilter = SKImageFilter.CreatePointLitDiffuse(
                                    new SKPoint3(location.X, location.Y, location.Z),
                                    new SKColor(lightColor.R, lightColor.G, lightColor.B, lightColor.A),
                                    surfaceScale,
                                    kd);

            canvas.DrawBitmap(b, (SKPoint)default, paint);

            CopyTo(bmp.Bytes, self.Data!, self.DataSize);
        }


        #endregion

        #region Resize

        public static Image<BGRA32> Resize(this Image<BGRA32> self, int width, int height, Quality quality)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));

            using var bmp = self.ToSKBitmap();
            using var newbmp = bmp.Resize(new SKSizeI(width, height), (SKFilterQuality)quality);

            return newbmp.ToImage32();
        }

        #endregion

        public static void ChromaKey(this Image<BGRA32> self, int value)
        {
            fixed (BGRA32* s = self.Data)
            {
                Parallel.For(0, self.Data.Length, new ChromaKeyProcess(s, s, value).Invoke);
            }
        }
        public static void ColorKey(this Image<BGRA32> self, BGRA32 color, int value)
        {
            fixed (BGRA32* s = self.Data)
            {
                Parallel.For(0, self.Data.Length, new ColorKeyProcess(s, s, color, value).Invoke);
            }
        }

        public static Image<BGRA32> Ellipse(int width, int height, int line, Color color)
        {
            return Ellipse(width, height, new()
            {
                StrokeWidth = line,
                Color = color,
                Style = BrushStyle.Stroke,
                IsAntialias = true
            });
        }
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

            using var paint = new SKPaint()
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = brush.IsAntialias,
                Style = (SKPaintStyle)brush.Style,
                StrokeWidth = min
            };

            canvas.DrawOval(
                new SKPoint(width / 2, height / 2),
                new SKSize(width / 2 - min / 2, height / 2 - min / 2),
                paint);

            return bmp.ToImage32();
        }
        public static Image<BGRA32> Ellipse(Size size, Brush brush)
            => Ellipse(size.Width, size.Height, brush);
        public static Image<BGRA32> Rect(int width, int height, int line, Color color)
        {
            return Rect(width, height, new()
            {
                StrokeWidth = line,
                Style = BrushStyle.Stroke,
                IsAntialias = true,
                Color = color
            });
        }
        public static Image<BGRA32> Rect(int width, int height, Brush brush)
        {
            var color = brush.Color;
            var line = brush.StrokeWidth;
            using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);

            using var paint = new SKPaint()
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = brush.IsAntialias,
                Style = (SKPaintStyle)brush.Style,
                StrokeWidth = line
            };

            canvas.DrawRect(
                0, 0,
                width, height,
                paint);

            return bmp.ToImage32();
        }
        public static Image<BGRA32> Rect(Size size, Brush brush)
        {
            return Rect(size.Width, size.Height, brush);
        }
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

            using var paint = new SKPaint()
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            canvas.DrawPath(path, paint);

            return bmp.ToImage32();
        }
        public static Image<BGRA32> RoundRect(int width, int height, int line, int radiusX, int radiusY, Color color)
        {
            return RoundRect(width, height, radiusX, radiusY, new()
            {
                StrokeWidth = line,
                Style = BrushStyle.Stroke,
                IsAntialias = true,
                Color = color
            });
        }
        public static Image<BGRA32> RoundRect(int width, int height, int radiusX, int radiusY, Brush brush)
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

            using var paint = new SKPaint()
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = min
            };

            canvas.DrawRoundRect(
                min / 2, min / 2,
                width - min, height - min,
                radiusX, radiusY,
                paint);

            return bmp.ToImage32();
        }
        public static Image<BGRA32> RoundRect(Size size, int radiusX, int radiusY, Brush brush)
        {
            return RoundRect(size.Width, size.Height, radiusX, radiusY, brush);
        }

        //Todo: 改行に対応する
        public static Image<BGRA32> Text(string text, Font font, float size, Color color)
        {
            if (string.IsNullOrEmpty(text)) return new Image<BGRA32>(1, 1, default(BGRA32));
            if (font is null) throw new ArgumentNullException(nameof(font));

            using var face = SKTypeface.FromFile(font.Filename);
            using var fontObj = new SKFont(face, size)
            {
                Edging = SKFontEdging.Antialias,

            };

            using var paint = new SKPaint(fontObj)
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = true,
            };

            var textBounds = new SKRect();
            paint.MeasureText(text, ref textBounds);


            using var bmp = new SKBitmap(new SKImageInfo((int)textBounds.Width, (int)textBounds.Height, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);

            float xText = textBounds.Width / 2 - textBounds.MidX;
            float yText = textBounds.Height / 2 - textBounds.MidY;

            canvas.DrawText(text, new SKPoint(xText, yText), paint);

            canvas.Flush();

            return bmp.ToImage32();
        }

        public static Image<BGRA32> StrokeText(string text, Font font, float size, float strokewidth, Color color)
        {
            if (string.IsNullOrEmpty(text)) return new Image<BGRA32>(1, 1, default(BGRA32));
            if (font is null) throw new ArgumentNullException(nameof(font));

            using var face = SKTypeface.FromFile(font.Filename);
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

            var textBounds = new SKRect();
            paint.MeasureText(text, ref textBounds);

            var p = strokewidth * 1.5;
            using var bmp = new SKBitmap(new SKImageInfo((int)(textBounds.Width + p), (int)(textBounds.Height + p), SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);

            float xText = textBounds.Width / 2 - textBounds.MidX;
            float yText = textBounds.Height / 2 - textBounds.MidY;

            canvas.DrawText(text, new SKPoint((float)(xText + (p / 2)), (float)(yText + (p / 2))), paint);

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
                for (int pos = 0; pos < number; pos++)
                {
                    var vertex = new Point(
                        (int)(Math.Sin(((pos + rotate) * (2 * Math.PI)) / number) * radiusX) + center.X,
                        (int)(Math.Cos(((pos + rotate) * (2 * Math.PI)) / number) * radiusY) + center.Y);

                    vertexes[pos] = vertex;
                }

                return vertexes;
            }
            catch (Exception)
            {
                throw;
            }
        }

        #region Encode
        public static bool Encode(this Image<BGRA32> self, byte[] buffer, EncodedImageFormat format, int quality = 100)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (buffer is null) throw new ArgumentNullException(nameof(buffer));
            self.ThrowIfDisposed();

            using var stream = new MemoryStream(buffer);

            return Encode(self, stream, format, quality);
        }
        public static bool Encode(this Image<BGR24> self, byte[] buffer, EncodedImageFormat format, int quality = 100)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (buffer is null) throw new ArgumentNullException(nameof(buffer));
            self.ThrowIfDisposed();

            using var stream = new MemoryStream(buffer);

            return Encode(self, stream, format, quality);
        }
        public static bool Encode(this Image<BGRA32> self, Stream stream, EncodedImageFormat format, int quality = 100)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            self.ThrowIfDisposed();

            using var bmp = self.ToSKBitmap();

            return bmp.Encode(stream, (SKEncodedImageFormat)format, quality);
        }
        public static bool Encode(this Image<BGR24> self, Stream stream, EncodedImageFormat format, int quality = 100)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            self.ThrowIfDisposed();

            using var bmp = self.ToSKBitmap();

            return bmp.Encode(stream, (SKEncodedImageFormat)format, quality);
        }
        public static bool Encode(this Image<BGRA32> self, string filename, EncodedImageFormat format, int quality = 100)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (filename is null) throw new ArgumentNullException(nameof(filename));
            self.ThrowIfDisposed();

            using var bmp = self.ToSKBitmap();
            using var stream = new FileStream(filename, FileMode.Create);

            return bmp.Encode(stream, (SKEncodedImageFormat)format, quality);
        }
        public static bool Encode(this Image<BGR24> self, string filename, EncodedImageFormat format, int quality = 100)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (filename is null) throw new ArgumentNullException(nameof(filename));
            self.ThrowIfDisposed();

            using var bmp = self.ToSKBitmap();
            using var stream = new FileStream(filename, FileMode.Create);

            return bmp.Encode(stream, (SKEncodedImageFormat)format, quality);
        }
        public static bool Encode(this Image<BGRA32> self, string filename, int quality = 100)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (filename is null) throw new ArgumentNullException(nameof(filename));
            self.ThrowIfDisposed();

            using var bmp = self.ToSKBitmap();
            using var stream = new FileStream(filename, FileMode.Create);
            var format = ToImageFormat(filename);

            return bmp.Encode(stream, (SKEncodedImageFormat)format, quality);
        }
        public static bool Encode(this Image<BGR24> self, string filename, int quality = 100)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (filename is null) throw new ArgumentNullException(nameof(filename));
            self.ThrowIfDisposed();

            using var bmp = self.ToSKBitmap();
            using var stream = new FileStream(filename, FileMode.Create);
            var format = ToImageFormat(filename);

            return bmp.Encode(stream, (SKEncodedImageFormat)format, quality);
        }
        #endregion

        #region Decode
        public static Image<BGRA32> Decode(ReadOnlySpan<byte> buffer)
        {
            using var bmp = SKBitmap.Decode(buffer);

            return bmp.ToImage32();
        }
        public static Image<BGRA32> Decode(byte[] buffer)
        {
            if (buffer is null) throw new ArgumentNullException(nameof(buffer));
            using var bmp = SKBitmap.Decode(buffer);

            return bmp.ToImage32();
        }
        public static Image<BGRA32> Decode(Stream stream)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));

            using var bmp = SKBitmap.Decode(stream);

            return bmp.ToImage32();
        }
        public static Image<BGRA32> Decode(string filename)
        {
            if (filename is null) throw new ArgumentNullException(nameof(filename));

            using var bmp = SKBitmap.Decode(filename);

            return bmp.ToImage32();
        }
        #endregion

        private static EncodedImageFormat ToImageFormat(string filename)
        {
            var ex = Path.GetExtension(filename);

            if (string.IsNullOrEmpty(filename)) throw new IOException(Resources.FileExtension);

            return ExtensionToFormat[ex];
        }
        private static readonly Dictionary<string, EncodedImageFormat> ExtensionToFormat = new()
        {
            { ".bmp", EncodedImageFormat.Bmp },
            { ".gif", EncodedImageFormat.Gif },
            { ".ico", EncodedImageFormat.Ico },
            { ".jpg", EncodedImageFormat.Jpeg },
            { ".jpeg", EncodedImageFormat.Jpeg },
            { ".png", EncodedImageFormat.Png },
            { ".wbmp", EncodedImageFormat.Wbmp },
            { ".webp", EncodedImageFormat.Webp },
            { ".pkm", EncodedImageFormat.Pkm },
            { ".ktx", EncodedImageFormat.Ktx },
            { ".astc", EncodedImageFormat.Astc },
            { ".dng", EncodedImageFormat.Dng },
            { ".heif", EncodedImageFormat.Heif },
        };

        public static Image<T2> Convert<T1, T2>(this Image<T1> self) where T2 : unmanaged, IPixel<T2> where T1 : unmanaged, IPixel<T1>, IPixelConvertable<T2>
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            self.ThrowIfDisposed();
            var dst = new Image<T2>(self.Width, self.Height, default(T2));

            fixed (T1* srcPtr = self.Data)
            fixed (T2* dstPtr = dst.Data)
            {
                Parallel.For(0, self.Data.Length, new ConvertToProcess<T1, T2>(srcPtr, dstPtr).Invoke);
            }

            return dst;
        }
    }
}
