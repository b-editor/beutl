using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using BEditor.Compute.Runtime;
using BEditor.Drawing.Pixel;
using BEditor.Drawing.Resources;

using SkiaSharp;

namespace BEditor.Drawing
{
    public static unsafe partial class Image
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

        public static void DrawImage<T>(this Image<T> self, Point point, Image<T> image, DrawingContext? context = null)
            where T : unmanaged, IPixel<T>, IGpuPixel<T>
        {
            if (context is null)
            {
                DrawImage(self, point, image);

                return;
            }

            if (self is null) throw new ArgumentNullException(nameof(self));
            if (image is null) throw new ArgumentNullException(nameof(image));
            self.ThrowIfDisposed();
            image.ThrowIfDisposed();
            var rect = new Rectangle(point, image.Size);
            var blended = self[rect];

            CLProgram program;
            var operation = (T)default;
            var key = operation.GetType().Name + "_blend";
            if (!context.Programs.ContainsKey(key))
            {
                program = context.Context.CreateProgram(operation.GetBlend());
                context.Programs.Add(key, program);
            }
            else
            {
                program = context.Programs[key];
            }

            using var kernel = program.CreateKernel("blend");

            var dataSize = image.DataSize;
            using var mask = context.Context.CreateMappingMemory(image.Data, dataSize);
            using var blended_ = context.Context.CreateMappingMemory(blended.Data, dataSize);
            kernel.NDRange(context.CommandQueue, new long[] { image.Width, image.Height }, blended_, mask);
            context.CommandQueue.WaitFinish();
            blended_.Read(context.CommandQueue, true, blended.Data, 0, dataSize).Wait();

            self[rect] = blended;
        }

        public static void DrawImage(this Image<BGRA32> self, Point point, Image<BGRA32> image)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (image is null) throw new ArgumentNullException(nameof(image));
            self.ThrowIfDisposed();
            image.ThrowIfDisposed();

            using var paint = new SKPaint { IsAntialias = true };
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

            using var paint = new SKPaint
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            canvas.DrawPath(path, paint);

            CopyTo(bmp.Bytes, self.Data, self.DataSize);
        }

        public static Image<BGRA32> Border(this Image<BGRA32> self, int size, BGRA32 color)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (size <= 0) throw new ArgumentException(string.Format(Strings.LessThan, nameof(size), 0));
            self.ThrowIfDisposed();

            var nwidth = self.Width + ((size + 5) * 2);
            var nheight = self.Height + ((size + 5) * 2);

            using var filter = SKImageFilter.CreateDilate(size, size);
            using var dilatePaint = new SKPaint { ImageFilter = filter, IsAntialias = true };
            using var paint = new SKPaint { IsAntialias = true };
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

        public static Image<BGRA32> Shadow(this Image<BGRA32> self, float x, float y, float blur, float opacity, BGRA32 color)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            self.ThrowIfDisposed();
            var w = self.Width + blur;
            var h = self.Height + blur;

            //キャンバスのサイズ
            var size_w = (Math.Abs(x) + (w / 2)) * 2;
            var size_h = (Math.Abs(y) + (h / 2)) * 2;

            using var filter = SKImageFilter.CreateDropShadow(x, y, blur, blur, new SKColor(color.R, color.G, color.B, (byte)(color.A * opacity)));
            using var paint = new SKPaint
            {
                ImageFilter = filter,
                IsAntialias = true
            };

            using var bmp = new SKBitmap((int)size_w, (int)size_h);
            using var canvas = new SKCanvas(bmp);
            using var d = self.ToSKBitmap();

            canvas.DrawBitmap(
                d,
                (size_w / 2) - (self.Width / 2),
                (size_h / 2) - (self.Height / 2),
                paint);

            return bmp.ToImage32();
        }

        public static Image<BGRA32> InnerShadow(this Image<BGRA32> self, float x, float y, float blur, float opacity, BGRA32 color, DrawingContext? context = null)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            self.ThrowIfDisposed();
            using var blurred = new Image<BGRA32>(self.Width, self.Height, new BGRA32(color.R, color.G, color.B, (byte)(255 * (opacity / 100))));
            using var mask = self.Clone();

            blurred.Mask(mask, new PointF(x, y), 0, true, context);
            Cv.Blur(blurred, (int)blur);

            blurred.Mask(mask, default, 0, false, context);
            var result = self.Clone();
            result.DrawImage(default, blurred, context);

            return result;
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

            using var paint = new SKPaint
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
            var pt = new SKPoint(center.X + (self.Width / 2), center.Y + (self.Height / 2));

            using var paint = new SKPaint
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

        public static void Mask(this Image<BGRA32> self, Image<BGRA32> mask, PointF point, float rotate, bool invert, DrawingContext? context = null)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (mask is null) throw new ArgumentNullException(nameof(mask));
            self.ThrowIfDisposed();
            mask.ThrowIfDisposed();

            // 回転した画像
            using var m = MakeMask(self.Size, mask, point, rotate);
            using var routed = m.ToImage32();
            if (!invert)
            {
                routed.ReverseOpacity(context);
            }

            self.AlphaSubtract(routed, context);
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

            using var paint = new SKPaint
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = brush.IsAntialias,
                Style = (SKPaintStyle)brush.Style,
                StrokeWidth = min
            };

            canvas.DrawOval(
                new SKPoint(width / 2, height / 2),
                new SKSize((width / 2) - (min / 2), (height / 2) - (min / 2)),
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
            var line = brush.StrokeWidth * 2;
            using var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);

            using var paint = new SKPaint
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

            using var paint = new SKPaint
            {
                Color = new SKColor(color.R, color.G, color.B, color.A),
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            canvas.DrawPath(path, paint);

            return bmp.ToImage32();
        }

        public static Image<BGRA32> RoundRect(int width, int height, int line, Color color, int topleft, int topright, int bottomleft, int bottomright)
        {
            static void Draw(Image<BGRA32> image, int line, int radius, int type, Color color)
            {
                var size = radius * 2;
                var x = type switch
                {
                    0 or 2 => 0,
                    1 or 3 => radius,
                    _ => 0,
                };
                var y = type switch
                {
                    0 or 1 => 0,
                    2 or 3 => radius,
                    _ => 0,
                };

                using var circle = Ellipse(size, size, line, color);
                using var round = circle[new Rectangle(x, y, radius, radius)];

                var xx = type switch
                {
                    0 or 2 => 0,
                    1 or 3 => image.Width - radius,
                    _ => 0,
                };
                var yy = type switch
                {
                    0 or 1 => 0,
                    2 or 3 => image.Height - radius,
                    _ => 0,
                };

                image[new Rectangle(xx, yy, radius, radius)] = round;
            }

            // 最大の半径
            var size_min = Math.Min(width, height) / 2;
            topleft = Math.Min(size_min, topleft);
            topright = Math.Min(size_min, topright);
            bottomleft = Math.Min(size_min, bottomleft);
            bottomright = Math.Min(size_min, bottomright);
            var result = Rect(width, height, line, color);

            // 左上
            Draw(result, line, topleft, 0, color);

            // 右上
            Draw(result, line, topright, 1, color);

            // 左下
            Draw(result, line, bottomleft, 2, color);

            // 右下
            Draw(result, line, bottomright, 3, color);

            return result;
        }

        public static Image<BGRA32> Text(
            string text,
            Font font,
            float size,
            Color color,
            HorizontalAlign hAlign,
            VerticalAlign vAlign,
            float linespace = 0,
            bool vertical = true)
        {
            if (string.IsNullOrEmpty(text)) return new Image<BGRA32>(1, 1, default(BGRA32));
            if (font is null) throw new ArgumentNullException(nameof(font));

            return TextHorizontal(text, font, size, color, hAlign, linespace);
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
                IsAntialias = true
            };

            using var linesBounds = new UnmanagedList<SKRect>();

            for (var i = 0; i < lines.Length; i++)
            {
                var item = lines[i];
                var textBounds = new SKRect();

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
                var textBounds = new SKRect();

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

        private static EncodedImageFormat ToImageFormat(string filename)
        {
            var ex = Path.GetExtension(filename);

            if (string.IsNullOrEmpty(filename)) throw new IOException(Strings.FileExtension);

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

        private static SKBitmap MakeMask(Size size, Image<BGRA32> mask, PointF point, float rotate)
        {
            using var paint = new SKPaint();
            var bmp = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);
            using var m = mask.ToSKBitmap();

            canvas.Translate(size.Width / 2, size.Height / 2);
            canvas.RotateDegrees(rotate);
            canvas.DrawBitmap(
                m,
                new SKPoint(
                    point.X - (mask.Width / 2F),
                    point.Y - (mask.Height / 2F)),
                paint);

            return bmp;
        }
    }
}