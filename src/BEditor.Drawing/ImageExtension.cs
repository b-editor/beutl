// ImageExtension.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

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
    /// <summary>
    /// Provides extension methods for processing images.
    /// </summary>
    public static unsafe partial class Image
    {
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

        /// <summary>
        /// Draws an image at the specified position.
        /// </summary>
        /// <typeparam name="T">The type of pixel.</typeparam>
        /// <param name="self">The <paramref name="image"/> will be drawn on this image.</param>
        /// <param name="point">The position to draw.</param>
        /// <param name="image">The image to be drawn on <paramref name="self"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> or <paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
        public static void DrawImage<T>(this Image<T> self, Point point, Image<T> image)
            where T : unmanaged, IPixel<T>
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

        /// <summary>
        /// Draws an image at the specified position.
        /// </summary>
        /// <typeparam name="T">The type of pixel.</typeparam>
        /// <param name="self">The <paramref name="image"/> will be drawn on this image.</param>
        /// <param name="point">The position to draw.</param>
        /// <param name="image">The image to be drawn on <paramref name="self"/>.</param>
        /// <param name="context">When processing using Gpu, specify a valid DrawingContext.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> or <paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
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

        /// <summary>
        /// Draws an image at the specified position.
        /// </summary>
        /// <param name="self">The <paramref name="image"/> will be drawn on this image.</param>
        /// <param name="point">The position to draw.</param>
        /// <param name="image">The image to be drawn on <paramref name="self"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> or <paramref name="image"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
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

        /// <summary>
        /// Draws an path at the specified position.
        /// </summary>
        /// <param name="self">The path will be drawn on this image.</param>
        /// <param name="color">The color of the path.</param>
        /// <param name="point">The position to draw.</param>
        /// <param name="points">The path to draw.</param>
        /// <exception cref="ArgumentNullException"><paramref name="self"/> or <paramref name="point"/> is <see langword="null"/>.</exception>
        /// <exception cref="ObjectDisposedException">Cannot access a disposed object.</exception>
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
                Style = SKPaintStyle.Fill,
            };

            canvas.DrawPath(path, paint);

            CopyTo(bmp.Bytes, self.Data, self.DataSize);
        }

        /// <summary>
        /// Generates an image with an round rectangle drawn on it.
        /// </summary>
        /// <param name="width">The width of the rectangle.</param>
        /// <param name="height">The height of the rectangle.</param>
        /// <param name="line">The line width of the rectangle.</param>
        /// <param name="color">The color of the rectangle.</param>
        /// <param name="topleft">The radius of the upper left corner.</param>
        /// <param name="topright">The radius of the upper right corner.</param>
        /// <param name="bottomleft">The radius of the lower left corner.</param>
        /// <param name="bottomright">The radius of the lower right corner.</param>
        /// <returns>Returns an image with an round rectangle drawn on it.</returns>
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

        /// <summary>
        /// Encodes an image and returns a value if it was successful.
        /// </summary>
        /// <param name="self">The image to encode.</param>
        /// <param name="buffer">The buffer for encoded data.</param>
        /// <param name="format">The format of the image to encode.</param>
        /// <param name="quality">The quality of the image.</param>
        /// <returns>Returns <see langword="true"/> if the encoding succeeds, <see langword="false"/> if it fails.</returns>
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
        public static Image<BGRA32> Decode(string filename)
        {
            if (filename is null) throw new ArgumentNullException(nameof(filename));

            using var bmp = SKBitmap.Decode(filename);

            return bmp.ToImage32();
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

        private static EncodedImageFormat ToImageFormat(string filename)
        {
            var ex = Path.GetExtension(filename);

            if (string.IsNullOrEmpty(filename)) throw new IOException(Strings.FileExtension);

            return ExtensionToFormat[ex];
        }
    }
}