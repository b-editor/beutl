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
    public sealed class GraphicsContextImpl : IGraphicsContextImpl
    {
        private readonly SKPaint _paint;
        private SKCanvas _canvas;
        private SKBitmap _bmp;

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

        public int Width { get; private set; }

        public int Height { get; private set; }

        public bool IsDisposed { get; private set; }

        public Camera Camera { get; set; }

        public Light? Light
        {
            get => default;
            set { }
        }

        public DepthStencilState DepthStencilState { get; set; } = DepthStencilState.Disabled;

        public void Clear()
        {
            _canvas.Clear();
        }

        public void Dispose()
        {
            if (IsDisposed) return;

            _canvas.Dispose();

            GC.SuppressFinalize(this);

            IsDisposed = true;
        }

        public void DrawBall(Ball ball)
        {
        }

        public void DrawCube(Cube cube)
        {
        }

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

        public void DrawTexture(Texture texture)
        {
            using var image = texture.ToImage();
            using var bmp = image.ToSKBitmap();
            SetTransform(texture);

            _canvas.DrawBitmap(bmp, -texture.Width / 2, -texture.Height / 2, _paint);

            ResetTransform();
        }

        public void MakeCurrent()
        {
        }

        public unsafe void ReadImage(Image<BGRA32> image)
        {
            fixed (BGRA32* dst = image.Data)
            {
                var size = image.DataSize;
                Buffer.MemoryCopy((void*)_bmp.GetPixels(), dst, size, size);
            }
        }

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
            if (mode is BlendMode.AlphaBlend) return SKBlendMode.SrcOver;
            else if (mode is BlendMode.Additive) return SKBlendMode.Plus;
            else if (mode is BlendMode.Subtract) return SKBlendMode.Difference;
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