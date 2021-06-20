// GraphicsContextImpl.cs
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

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics.Platform;

using SkiaSharp;

namespace BEditor.Graphics.Skia
{
    /// <summary>
    /// Represents the graphics context.
    /// </summary>
    public sealed class GraphicsContextImpl : IGraphicsContextImpl
    {
        private readonly SKPaint _paint;
        private SKCanvas _canvas;
        private SKBitmap _bmp;

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphicsContextImpl"/> class.
        /// </summary>
        /// <param name="width">The width of the graphics context.</param>
        /// <param name="height">The height of the graphics context.</param>
        public GraphicsContextImpl(int width, int height)
        {
            Width = width;
            Height = height;

            Camera = new OrthographicCamera(new(0, 0, 1024), width, height);
            _bmp = new(new SKImageInfo(width, height, SKColorType.Bgra8888));
            _canvas = new(_bmp);
            _paint = new();
            _canvas.Translate(width / 2, height / 2);
        }

        /// <inheritdoc/>
        public int Width { get; private set; }

        /// <inheritdoc/>
        public int Height { get; private set; }

        /// <inheritdoc/>
        public bool IsDisposed { get; private set; }

        /// <inheritdoc/>
        public Camera Camera { get; set; }

        /// <inheritdoc/>
        public Light? Light { get; set; }

        /// <inheritdoc/>
        public void Clear()
        {
            _canvas.Clear();
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (IsDisposed) return;

            _canvas.Dispose();

            GC.SuppressFinalize(this);

            IsDisposed = true;
        }

        /// <inheritdoc/>
        public void DrawBall(Ball ball)
        {
        }

        /// <inheritdoc/>
        public void DrawCube(Cube cube)
        {
        }

        /// <inheritdoc/>
        public void DrawLine(Line line)
        {
            _paint.StrokeWidth = line.Width;
            SetTransform(line);

            _canvas.DrawLine(
                new SKPoint(line.Start.X, line.Start.Y),
                new SKPoint(line.End.X, line.End.Y),
                _paint);

            ResetTransform();
        }

        /// <inheritdoc/>
        public void DrawTexture(Texture texture)
        {
            using var image = texture.ToImage();
            using var bmp = image.ToSKBitmap();
            SetTransform(texture);

            _canvas.DrawBitmap(bmp, -texture.Width / 2, -texture.Height / 2, _paint);

            ResetTransform();
        }

        /// <inheritdoc/>
        public void MakeCurrent()
        {
        }

        /// <inheritdoc/>
        public unsafe void ReadImage(Image<BGRA32> image)
        {
            fixed (BGRA32* dst = image.Data)
            {
                var size = image.DataSize;
                Buffer.MemoryCopy((void*)_bmp.GetPixels(), dst, size, size);
            }
        }

        /// <inheritdoc/>
        public void SetSize(Size size)
        {
            _canvas.Dispose();
            _bmp.Dispose();
            _bmp = new(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888));
            _canvas = new(_bmp);
            Width = size.Width;
            Height = size.Height;
            ResetTransform();
        }

        private static SKBlendMode ToSkBlendMode(BlendMode mode)
        {
            if (mode is BlendMode.Default) return SKBlendMode.SrcOver;
            else if (mode is BlendMode.Add) return SKBlendMode.Plus;
            else if (mode is BlendMode.Suntract) return SKBlendMode.Difference;
            else if (mode is BlendMode.Multiplication) return SKBlendMode.Multiply;

            return SKBlendMode.SrcOver;
        }

        private void SetTransform(Drawable drawable)
        {
            _paint.Color = new(drawable.Color.R, drawable.Color.G, drawable.Color.B, drawable.Color.A);
            _paint.BlendMode = ToSkBlendMode(drawable.BlendMode);

            _canvas.Translate(drawable.Transform.Coordinate.X, -drawable.Transform.Coordinate.Y);
            _canvas.RotateDegrees(drawable.Transform.Rotate.Z);
            _canvas.Scale(drawable.Transform.Scale.X, drawable.Transform.Scale.Y);
            _canvas.Translate(drawable.Transform.Center.X, -drawable.Transform.Center.Y);
        }

        private void ResetTransform()
        {
            _canvas.ResetMatrix();
            _canvas.Translate(Width / 2, Height / 2);
        }
    }
}