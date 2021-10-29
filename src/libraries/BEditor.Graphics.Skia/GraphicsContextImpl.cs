using System;
using System.Linq;
using System.Runtime.InteropServices;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics.Platform;

using SkiaSharp;

namespace BEditor.Graphics.Skia
{
    public sealed class GraphicsContextImpl : IGraphicsContextImpl
    {
        private readonly SKColor[] _textureColors = new SKColor[4];
        private readonly SKPoint[] _textureVertices = new SKPoint[4];
        private readonly SKPoint[] _textureTexs = new SKPoint[4];
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

            Array.Fill(_textureColors, SKColors.White);
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
            SetTransform(cube);

            _canvas.DrawRect(-cube.Width / 2, -cube.Height / 2, cube.Width, cube.Height, _paint);

            ResetTransform();
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
            lock (this)
            {
                using var image = texture.ToImage();
                using var bmp = image.ToSKBitmap();
                using var shader = SKShader.CreateBitmap(bmp);

                for (var i = 0; i < 4; i++)
                {
                    ref var item = ref texture.Vertices[i];
                    ref var vertex = ref _textureVertices[i];
                    ref var tex = ref _textureTexs[i];
                    vertex.X = item.PosX;
                    vertex.Y = -item.PosY;

                    tex.X = item.TexU * texture.Width;
                    tex.Y = item.TexV * texture.Height;
                }

                _paint.Shader = shader;

                SetTransform(texture);

                _canvas.DrawVertices(SKVertexMode.TriangleFan, _textureVertices, _textureTexs, _textureColors, _paint);

                ResetTransform();
                _paint.Shader = null;
            }
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
            var transform = drawable.Transform;

            _canvas.Translate(transform.Position.X + transform.Relative.X, -transform.Position.Y + transform.Relative.Y);
            _canvas.RotateDegrees(transform.Rotation.Z);
            _canvas.Scale(transform.Scale.X, transform.Scale.Y);
            _canvas.Translate(transform.Center.X, -transform.Center.Y);
        }

        private void ResetTransform()
        {
            _canvas.ResetMatrix();
            _canvas.Translate(Width / 2, Height / 2);
        }
    }
}