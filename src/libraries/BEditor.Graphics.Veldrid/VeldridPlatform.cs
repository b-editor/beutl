using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics.Mock;
using BEditor.Graphics.Platform;

namespace BEditor.Graphics.Veldrid
{
    public class VeldridPlatform : IPlatform
    {
        public IBallImpl CreateBall(float radiusX, float radiusY, float radiusZ)
        {
            throw new NotImplementedException();
        }

        public IGraphicsContextImpl CreateContext(int width, int height)
        {
            return new GraphicsContextImpl(width, height);
        }

        public ICubeImpl CreateCube(float width, float height, float depth)
        {
            return new CubeImpl(width, height, depth);
        }

        public ILineImpl CreateLine(Vector3 start, Vector3 end, float width)
        {
            return new LineImpl(start, end, width);
        }

        public ITextureImpl CreateTexture(Image<BGRA32> image, VertexPositionTexture[]? vertices = null)
        {
            var halfH = image.Height / 2;
            var halfW = image.Width / 2;
            vertices ??= new VertexPositionTexture[]
            {
                new(new(halfW, -halfH, 0), new(1, 1)),
                new(new(halfW, halfH, 0), new(1, 0)),
                new(new(-halfW, halfH, 0), new(0, 0)),
                new(new(-halfW, -halfH, 0), new(0, 1)),
            };

            return new TextureImpl(image, vertices);
        }
    }
}
