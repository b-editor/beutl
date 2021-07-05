// SkiaEffects.cs
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
using BEditor.Drawing.Resources;

using OpenCvSharp;

using SkiaSharp;

namespace BEditor.Drawing
{
    /// <inheritdoc cref="Image"/>
    public static partial class Image
    {
        /// <summary>
        /// Apply a shadow to the image.
        /// </summary>
        /// <param name="self">The image to be shadowed.</param>
        /// <param name="x">The x-coordinate of the shadow.</param>
        /// <param name="y">The y-coordinate of the shadow.</param>
        /// <param name="blur">The shadow sigma.</param>
        /// <param name="opacity">The opacity of the shadow.</param>
        /// <param name="color">The color of the shadow.</param>
        /// <returns>Returns an image with a shadow for <paramref name="self"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static Image<BGRA32> Shadow(this Image<BGRA32> self, float x, float y, float blur, float opacity, BGRA32 color)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            self.ThrowIfDisposed();
            var w = self.Width + blur;
            var h = self.Height + blur;

            // キャンバスのサイズ
            var size_w = (Math.Abs(x) + (w / 2)) * 2;
            var size_h = (Math.Abs(y) + (h / 2)) * 2;

            using var filter = SKImageFilter.CreateDropShadow(x, y, blur, blur, new SKColor(color.R, color.G, color.B, (byte)(color.A * opacity)));
            using var paint = new SKPaint
            {
                ImageFilter = filter,
                IsAntialias = true,
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

        /// <summary>
        /// Apply a inner shadow to the image.
        /// </summary>
        /// <param name="self">The image to be shadowed.</param>
        /// <param name="x">The x-coordinate of the shadow.</param>
        /// <param name="y">The y-coordinate of the shadow.</param>
        /// <param name="blur">The shadow sigma.</param>
        /// <param name="opacity">The opacity of the shadow.</param>
        /// <param name="color">The color of the shadow.</param>
        /// <param name="context">When processing using Gpu, specify a valid DrawingContext.</param>
        /// <returns>Returns an image with a shadow for <paramref name="self"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static Image<BGRA32> InnerShadow(this Image<BGRA32> self, float x, float y, float blur, float opacity, BGRA32 color, DrawingContext? context = null)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            self.ThrowIfDisposed();
            using var blurred = new Image<BGRA32>(self.Width, self.Height, new BGRA32(color.R, color.G, color.B, (byte)(255 * (opacity / 100))));
            using var mask = self.Clone();

            blurred.Mask(mask, new PointF(x, y), 0, true, context);
            Cv.Blur(blurred, new Size((int)blur, (int)blur));

            blurred.Mask(mask, default, 0, false, context);
            var result = self.Clone();
            result.DrawImage(default, blurred, context);

            return result;
        }

        /// <summary>
        /// Blurs the image.
        /// </summary>
        /// <param name="self">The image to be blurred.</param>
        /// <param name="sigma">The Gaussian sigma value for blurring.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Blur(this Image<BGRA32> self, float sigma)
        {
            self.Blur(sigma, sigma);
        }

        /// <summary>
        /// Blurs the image.
        /// </summary>
        /// <param name="self">The image to be blurred.</param>
        /// <param name="sigmaX">The Gaussian sigma value for blurring along the X axis.</param>
        /// <param name="sigmaY">The Gaussian sigma value for blurring along the Y axis.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
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

        /// <summary>
        /// Blurs the image.
        /// </summary>
        /// <param name="self">The image to be blurred.</param>
        /// <param name="sigma">The Gaussian sigma value for blurring.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Blur(this Image<BGR24> self, float sigma)
        {
            self.Blur(sigma, sigma);
        }

        /// <summary>
        /// Blurs the image.
        /// </summary>
        /// <param name="self">The image to be blurred.</param>
        /// <param name="sigmaX">The Gaussian sigma value for blurring along the X axis.</param>
        /// <param name="sigmaY">The Gaussian sigma value for blurring along the Y axis.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
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

        /// <summary>
        /// Dilates the image.
        /// </summary>
        /// <param name="self">The image to dilate.</param>
        /// <param name="radius">The distance to dilate to either side of each pixel.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Dilate(this Image<BGRA32> self, int radius)
        {
            self.Dilate(radius, radius);
        }

        /// <summary>
        /// Dilates the image.
        /// </summary>
        /// <param name="self">The image to dilate.</param>
        /// <param name="radiusX">The distance to dilate along the x axis to either side of each pixel.</param>
        /// <param name="radiusY">The distance to dilate along the y axis to either side of each pixel.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
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

        /// <summary>
        /// Dilates the image.
        /// </summary>
        /// <param name="self">The image to dilate.</param>
        /// <param name="radius">The distance to dilate to either side of each pixel.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Dilate(this Image<BGR24> self, int radius)
        {
            self.Dilate(radius, radius);
        }

        /// <summary>
        /// Dilates the image.
        /// </summary>
        /// <param name="self">The image to dilate.</param>
        /// <param name="radiusX">The distance to dilate along the x axis to either side of each pixel.</param>
        /// <param name="radiusY">The distance to dilate along the y axis to either side of each pixel.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
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

        /// <summary>
        /// Erodes the image.
        /// </summary>
        /// <param name="self">The image to erode.</param>
        /// <param name="radius">The distance to erode to either side of each pixel.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Erode(this Image<BGRA32> self, int radius)
        {
            self.Erode(radius, radius);
        }

        /// <summary>
        /// Erodes the image.
        /// </summary>
        /// <param name="self">The image to erode.</param>
        /// <param name="radiusX">The distance to erode along the x axis to either side of each pixel.</param>
        /// <param name="radiusY">The distance to erode along the y axis to either side of each pixel.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Erode(this Image<BGRA32> self, int radiusX, int radiusY)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            self.ThrowIfDisposed();

            using var filter = SKImageFilter.CreateErode(radiusX, radiusY);
            using var paint = new SKPaint { ImageFilter = filter, IsAntialias = true };
            using var bmp = new SKBitmap(new(self.Width, self.Height, SKColorType.Bgra8888));
            using var canvas = new SKCanvas(bmp);
            using var b = self.ToSKBitmap();

            canvas.DrawBitmap(b, 0, 0, paint);

            CopyTo(bmp.Bytes, self.Data!, self.DataSize);
        }

        /// <summary>
        /// Erodes the image.
        /// </summary>
        /// <param name="self">The image to erode.</param>
        /// <param name="radius">The distance to erode to either side of each pixel.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void Erode(this Image<BGR24> self, int radius)
        {
            self.Erode(radius, radius);
        }

        /// <summary>
        /// Erodes the image.
        /// </summary>
        /// <param name="self">The image to erode.</param>
        /// <param name="radiusX">The distance to erode along the x axis to either side of each pixel.</param>
        /// <param name="radiusY">The distance to erode along the y axis to either side of each pixel.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
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

        /// <summary>
        /// Applies a linear gradient.
        /// </summary>
        /// <param name="self">The image to combine linear gradients.</param>
        /// <param name="start">The percent of the starting position of the gradient.</param>
        /// <param name="end">The percent of the ending position of the gradient.</param>
        /// <param name="colors">The colors of the gradient.</param>
        /// <param name="anchors">The anchors of the gradient.</param>
        /// <param name="mode">The shader mode.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> or <paramref name="colors"/> or <paramref name="anchors"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
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
                IsAntialias = true,
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

        /// <summary>
        /// Applies a circular gradient.
        /// </summary>
        /// <param name="self">The image to combine circular gradients.</param>
        /// <param name="center">The center position of the gradient.</param>
        /// <param name="radius">The radius of the gradient.</param>
        /// <param name="colors">The colors of the gradient.</param>
        /// <param name="anchors">The anchors of the gradient.</param>
        /// <param name="mode">The shader mode.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> or <paramref name="colors"/> or <paramref name="anchors"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void CircularGradient(this Image<BGRA32> self, PointF center, float radius, IEnumerable<Color> colors, IEnumerable<float> anchors, ShaderTileMode mode)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (colors is null) throw new ArgumentNullException(nameof(colors));
            if (anchors is null) throw new ArgumentNullException(nameof(anchors));
            self.ThrowIfDisposed();
            self.SetColor(new BGRA32(255, 255, 255, 255));
            var pt = new SKPoint(center.X + (self.Width / 2), center.Y + (self.Height / 2));

            using var paint = new SKPaint
            {
                BlendMode = SKBlendMode.Modulate,
                IsAntialias = true,
            };
            using var bmp = self.ToSKBitmap();
            using var canvas = new SKCanvas(bmp);

            paint.Shader = SKShader.CreateRadialGradient(
                pt,
                radius,
                colors.Select(c => new SKColor(c.R, c.G, c.B, c.A)).ToArray(),
                anchors.ToArray(),
                (SKShaderTileMode)mode);

            canvas.DrawRect(0, 0, self.Width, self.Height, paint);

            CopyTo(bmp.Bytes, self.Data!, self.DataSize);
        }

        /// <summary>
        /// Applies point light diffusion.
        /// </summary>
        /// <param name="self">The image to apply the effect to.</param>
        /// <param name="location">The position of point light.</param>
        /// <param name="lightColor">The color of light.</param>
        /// <param name="surfaceScale">Scale factor to transform from alpha values to physical height.</param>
        /// <param name="kd">Diffuse reflectance coefficient.</param>
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

        /// <summary>
        /// Resizes the image.
        /// </summary>
        /// <param name="self">The image to resize.</param>
        /// <param name="width">The width of the new image.</param>
        /// <param name="height">The height of the new image.</param>
        /// <param name="quality">The quality of interpolation.</param>
        /// <returns>The image resized to the specified size.</returns>
        public static Image<BGRA32> Resize(this Image<BGRA32> self, int width, int height, Quality quality)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));

            using var bmp = self.ToSKBitmap();
            using var newbmp = bmp.Resize(new SKSizeI(width, height), (SKFilterQuality)quality);

            return newbmp.ToImage32();
        }
    }
}