using System;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics.Platform;

using Veldrid;

namespace BEditor.Graphics.Veldrid
{
    public sealed class TextureImpl : DrawableImpl, ITextureImpl
    {
        private readonly Image<BGRA32> _image;

        public TextureImpl(Image<BGRA32> image, VertexPositionTexture[]? vertices = null)
        {
            _image = image.Clone();
            var halfH = image.Height / 2;
            var halfW = image.Width / 2;

            Vertices = vertices ?? new VertexPositionTexture[]
            {
                 new(new(halfW, -halfH, 0), new(1, 1)),
                new(new(halfW, halfH, 0), new(1, 0)),
                new(new(-halfW, halfH, 0), new(0, 0)),
                new(new(-halfW, -halfH, 0), new(0, 1)),
            };
        }

        public int Width => _image.Width;

        public int Height => _image.Height;

        public ReadOnlyMemory<VertexPositionTexture> Vertices { get; }

        public Image<BGRA32> ToImage()
        {
            return _image.Clone();
        }

        public TextureDescription ToTextureDescription()
        {
            return TextureDescription.Texture2D((uint)Width, (uint)Height, 1, 1, PixelFormat.B8_G8_R8_A8_UNorm, TextureUsage.Sampled);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _image.Dispose();
        }
    }
}
