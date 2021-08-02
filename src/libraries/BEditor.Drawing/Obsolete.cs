// Obsolete.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing.Pixel;
using BEditor.Drawing.PixelOperation;

using OpenCvSharp;

using SkiaSharp;

namespace BEditor.Drawing
{
#pragma warning disable SA1402
    /// <inheritdoc cref="Color"/>
    public partial struct Color
    {
        /// <summary>
        /// Creates a <see cref="Color"/> structure from a 32-bit ARGB value.
        /// </summary>
        /// <param name="argb">A value specifying the 32-bit ARGB value.</param>
        /// <returns>The <see cref="Color"/> structure that this method creates.</returns>
        [Obsolete("Use FromInt32.")]
        public static Color FromARGB(int argb)
        {
            return FromARGB(unchecked((uint)argb));
        }

        /// <summary>
        /// Creates a <see cref="Color"/> structure from a 32-bit ARGB value.
        /// </summary>
        /// <param name="argb">A value specifying the 32-bit ARGB value.</param>
        /// <returns>The <see cref="Color"/> structure that this method creates.</returns>
        [Obsolete("Use FromUInt32.")]
        public static Color FromARGB(uint argb)
        {
            long color = argb;
            return new(
                unchecked((byte)(color >> ARGBAlphaShift)),
                unchecked((byte)(color >> ARGBRedShift)),
                unchecked((byte)(color >> ARGBGreenShift)),
                unchecked((byte)(color >> ARGBBlueShift)));
        }

        /// <summary>
        /// Creates a <see cref="Color"/> structure from the four ARGB component.
        /// </summary>
        /// <param name="a">The alpha component.</param>
        /// <param name="r">The red component.</param>
        /// <param name="g">The green component.</param>
        /// <param name="b">The blue component.</param>
        /// <returns>The <see cref="Color"/> that this method creates.</returns>
        [Obsolete("Use FromArgb.")]
        public static Color FromARGB(byte a, byte r, byte g, byte b)
        {
            return new(a, r, g, b);
        }

        /// <summary>
        /// Creates a <see cref="Color"/> structure from the html color.
        /// </summary>
        /// <param name="htmlcolor">The value that specifies the html color.</param>
        /// <returns>The <see cref="Color"/> that this method creates.</returns>
        [Obsolete("Use Parse.")]
        public static Color FromHTML(string? htmlcolor)
        {
            return Parse(htmlcolor);
        }
    }

    /// <inheritdoc cref="Cv"/>
    public static partial class Cv
    {
        /// <summary>
        /// Blurs an image using a Gaussian filter.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="kernelSize">The smoothing kernel size.</param>
        [Obsolete("Use GaussianBlur(Image<BGRA32>, Size, double, double)")]
        public static void GaussianBlur(Image<BGRA32> image, int kernelSize)
        {
            using var mat = image.ToMat();

            if (kernelSize % 2 == 0)
            {
                kernelSize++;
            }

            Cv2.GaussianBlur(mat, mat, new(kernelSize, kernelSize), 0, 0);
        }

        /// <summary>
        /// Smoothes image using normalized box filter.
        /// </summary>
        /// <param name="image">The image to apply the effect to.</param>
        /// <param name="kernelSize">The smoothing kernel size.</param>
        [Obsolete("Use Blur(Image<BGRA32>, Size)")]
        public static void Blur(Image<BGRA32> image, int kernelSize)
        {
            using var mat = image.ToMat();

            if (kernelSize % 2 == 0)
            {
                kernelSize++;
            }

            Cv2.Blur(mat, mat, new(kernelSize, kernelSize));
        }
    }

    /// <inheritdoc cref="Image"/>
    public static unsafe partial class Image
    {
        /// <summary>
        /// Encodes an image and returns a value if it was successful.
        /// </summary>
        /// <param name="self">The image to encode.</param>
        /// <param name="buffer">The buffer for encoded data.</param>
        /// <param name="format">The format of the image to encode.</param>
        /// <param name="quality">The quality of the image.</param>
        /// <returns>Returns <see langword="true"/> if the encoding succeeds, <see langword="false"/> if it fails.</returns>
        [Obsolete("Use Image<T>.Save.")]
        public static bool Encode(this Image<BGRA32> self, byte[] buffer, EncodedImageFormat format, int quality = 100)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (buffer is null) throw new ArgumentNullException(nameof(buffer));
            self.ThrowIfDisposed();

            using var stream = new MemoryStream(buffer);

            return Encode(self, stream, format, quality);
        }

        /// <summary>
        /// Encodes an image and returns a value if it was successful.
        /// </summary>
        /// <param name="self">The image to encode.</param>
        /// <param name="buffer">The buffer for encoded data.</param>
        /// <param name="format">The format of the image to encode.</param>
        /// <param name="quality">The quality of the image.</param>
        /// <returns>Returns <see langword="true"/> if the encoding succeeds, <see langword="false"/> if it fails.</returns>
        [Obsolete("Use Image<T>.Save.")]
        public static bool Encode(this Image<BGR24> self, byte[] buffer, EncodedImageFormat format, int quality = 100)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (buffer is null) throw new ArgumentNullException(nameof(buffer));
            self.ThrowIfDisposed();

            using var stream = new MemoryStream(buffer);

            return Encode(self, stream, format, quality);
        }

        /// <summary>
        /// Encodes an image and returns a value if it was successful.
        /// </summary>
        /// <param name="self">The image to encode.</param>
        /// <param name="stream">The buffer for encoded data.</param>
        /// <param name="format">The format of the image to encode.</param>
        /// <param name="quality">The quality of the image.</param>
        /// <returns>Returns <see langword="true"/> if the encoding succeeds, <see langword="false"/> if it fails.</returns>
        [Obsolete("Use Image<T>.Save.")]
        public static bool Encode(this Image<BGRA32> self, Stream stream, EncodedImageFormat format, int quality = 100)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            self.ThrowIfDisposed();

            using var bmp = self.ToSKBitmap();

            return bmp.Encode(stream, (SKEncodedImageFormat)format, quality);
        }

        /// <summary>
        /// Encodes an image and returns a value if it was successful.
        /// </summary>
        /// <param name="self">The image to encode.</param>
        /// <param name="stream">The buffer for encoded data.</param>
        /// <param name="format">The format of the image to encode.</param>
        /// <param name="quality">The quality of the image.</param>
        /// <returns>Returns <see langword="true"/> if the encoding succeeds, <see langword="false"/> if it fails.</returns>
        [Obsolete("Use Image<T>.Save.")]
        public static bool Encode(this Image<BGR24> self, Stream stream, EncodedImageFormat format, int quality = 100)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (stream is null) throw new ArgumentNullException(nameof(stream));
            self.ThrowIfDisposed();

            using var bmp = self.ToSKBitmap();

            return bmp.Encode(stream, (SKEncodedImageFormat)format, quality);
        }

        /// <summary>
        /// Encodes an image and returns a value if it was successful.
        /// </summary>
        /// <param name="self">The image to encode.</param>
        /// <param name="filename">The buffer for encoded data.</param>
        /// <param name="format">The format of the image to encode.</param>
        /// <param name="quality">The quality of the image.</param>
        /// <returns>Returns <see langword="true"/> if the encoding succeeds, <see langword="false"/> if it fails.</returns>
        [Obsolete("Use Image<T>.Save.")]
        public static bool Encode(this Image<BGRA32> self, string filename, EncodedImageFormat format, int quality = 100)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (filename is null) throw new ArgumentNullException(nameof(filename));
            self.ThrowIfDisposed();

            using var bmp = self.ToSKBitmap();
            using var stream = new FileStream(filename, FileMode.Create);

            return bmp.Encode(stream, (SKEncodedImageFormat)format, quality);
        }

        /// <summary>
        /// Encodes an image and returns a value if it was successful.
        /// </summary>
        /// <param name="self">The image to encode.</param>
        /// <param name="filename">The buffer for encoded data.</param>
        /// <param name="format">The format of the image to encode.</param>
        /// <param name="quality">The quality of the image.</param>
        /// <returns>Returns <see langword="true"/> if the encoding succeeds, <see langword="false"/> if it fails.</returns>
        [Obsolete("Use Image<T>.Save.")]
        public static bool Encode(this Image<BGR24> self, string filename, EncodedImageFormat format, int quality = 100)
        {
            if (self is null) throw new ArgumentNullException(nameof(self));
            if (filename is null) throw new ArgumentNullException(nameof(filename));
            self.ThrowIfDisposed();

            using var bmp = self.ToSKBitmap();
            using var stream = new FileStream(filename, FileMode.Create);

            return bmp.Encode(stream, (SKEncodedImageFormat)format, quality);
        }

        /// <summary>
        /// Encodes an image and returns a value if it was successful.
        /// </summary>
        /// <param name="self">The image to encode.</param>
        /// <param name="filename">The buffer for encoded data.</param>
        /// <param name="quality">The quality of the image.</param>
        /// <returns>Returns <see langword="true"/> if the encoding succeeds, <see langword="false"/> if it fails.</returns>
        [Obsolete("Use Image<T>.Save.")]
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

        /// <summary>
        /// Encodes an image and returns a value if it was successful.
        /// </summary>
        /// <param name="self">The image to encode.</param>
        /// <param name="filename">The buffer for encoded data.</param>
        /// <param name="quality">The quality of the image.</param>
        /// <returns>Returns <see langword="true"/> if the encoding succeeds, <see langword="false"/> if it fails.</returns>
        [Obsolete("Use Image<T>.Save.")]
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

        /// <summary>
        /// Decodes an image using the specified buffer.
        /// </summary>
        /// <param name="buffer">The buffer of the image to decode.</param>
        /// <returns>Returns the decoded image.</returns>
        [Obsolete("Don't use this method.")]
        public static Image<BGRA32> Decode(ReadOnlySpan<byte> buffer)
        {
            using var bmp = SKBitmap.Decode(buffer);

            return bmp.ToImage32();
        }

        /// <summary>
        /// Decodes an image using the specified buffer.
        /// </summary>
        /// <param name="buffer">The buffer of the image to decode.</param>
        /// <returns>Returns the decoded image.</returns>
        [Obsolete("Don't use this method.")]
        public static Image<BGRA32> Decode(byte[] buffer)
        {
            if (buffer is null) throw new ArgumentNullException(nameof(buffer));
            using var bmp = SKBitmap.Decode(buffer);

            return bmp.ToImage32();
        }

        /// <summary>
        /// Decodes an image using the specified stream.
        /// </summary>
        /// <param name="stream">The stream of the image to decode.</param>
        /// <returns>Returns the decoded image.</returns>
        [Obsolete("Use Image<T>.FromStream.")]
        public static Image<BGRA32> Decode(Stream stream)
        {
            if (stream is null) throw new ArgumentNullException(nameof(stream));

            using var bmp = SKBitmap.Decode(stream);

            return bmp.ToImage32();
        }

        /// <summary>
        /// Decodes an image using the specified filename.
        /// </summary>
        /// <param name="filename">The filename of the image to decode.</param>
        /// <returns>Returns the decoded image.</returns>
        [Obsolete("Use Image<T>.FromFile.")]
        public static Image<BGRA32> Decode(string filename)
        {
            if (filename is null) throw new ArgumentNullException(nameof(filename));

            using var bmp = SKBitmap.Decode(filename);

            return bmp.ToImage32();
        }

        /// <summary>
        /// Converts the <see cref="Image{T}"/>.
        /// </summary>
        /// <typeparam name="T1">The type of pixel before conversion.</typeparam>
        /// <typeparam name="T2">The type of pixel after conversion.</typeparam>
        /// <param name="image">The image to convert.</param>
        /// <returns>Returns the converted image.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        [Obsolete("Don't use this method.")]
        public static Image<T2> Convert<T1, T2>(this Image<T1> image)
            where T1 : unmanaged, IPixel<T1>, IPixelConvertable<T2>
            where T2 : unmanaged, IPixel<T2>
        {
            if (image is null) throw new ArgumentNullException(nameof(image));
            image.ThrowIfDisposed();
            var dst = new Image<T2>(image.Width, image.Height, default(T2));

            fixed (T1* srcPtr = image.Data)
            fixed (T2* dstPtr = dst.Data)
            {
                PixelOperate(image.Data.Length, new ConvertToOperation<T1, T2>(srcPtr, dstPtr));
            }

            return dst;
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
        [Obsolete("Use FormattedText.")]
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
            var lineHeight = fontObj.Metrics.Descent - fontObj.Metrics.Ascent;

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

            using var bmp = new SKBitmap(new SKImageInfo((int)linesBounds.Max(i => i.Width), (int)((lineHeight * lines.Length) + linespace), SKColorType.Bgra8888));
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

                    canvas.Translate(-x, lineHeight + linespace);
                }
                else if (hAlign is HorizontalAlign.Left)
                {
                    canvas.DrawPath(path, paint);

                    canvas.Translate(0, lineHeight + linespace);
                }
                else
                {
                    var x = (bmp.Width - bounds.Width) / 2;
                    canvas.Translate(x, 0);
                    canvas.DrawPath(path, paint);

                    canvas.Translate(-x, lineHeight + linespace);
                }
            }

            canvas.Flush();

            return bmp.ToImage32();
        }
    }
#pragma warning restore SA1402
}