// OpenGLPlatform.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics.Platform;

namespace BEditor.Graphics.OpenGL
{
    /// <summary>
    /// Represents the OpenGL platform.
    /// </summary>
    public class OpenGLPlatform : IPlatform
    {
        /// <inheritdoc/>
        public IBallImpl CreateBall(float radiusX, float radiusY, float radiusZ)
        {
            return new BallImpl(radiusX, radiusY, radiusZ);
        }

        /// <inheritdoc/>
        public IGraphicsContextImpl CreateContext(int width, int height)
        {
            return new GraphicsContextImpl(width, height);
        }

        /// <inheritdoc/>
        public ICubeImpl CreateCube(float width, float height, float depth)
        {
            return new CubeImpl(width, height, depth);
        }

        /// <inheritdoc/>
        public ILineImpl CreateLine(Vector3 start, Vector3 end, float width)
        {
            return new LineImpl(start, end, width);
        }

        /// <inheritdoc/>
        public ITextureImpl CreateTexture(Image<BGRA32> image, Vector3[]? vertices = null, Vector2[]? uv = null)
        {
            var halfH = image.Height / 2;
            var halfW = image.Width / 2;
            vertices ??= new Vector3[]
            {
                new(halfW, -halfH, 0),
                new(halfW, halfH, 0),
                new(-halfW, halfH, 0),
                new(-halfW, -halfH, 0),
            };
            uv ??= new Vector2[]
            {
                new(1, 1),
                new(1, 0),
                new(0, 0),
                new(0, 1),
            };

            var ver = new float[]
            {
                vertices[0].X, vertices[0].Y, vertices[0].Z, uv[0].X, uv[0].Y,
                vertices[1].X, vertices[1].Y, vertices[1].Z, uv[1].X, uv[1].Y,
                vertices[2].X, vertices[2].Y, vertices[2].Z, uv[2].X, uv[2].Y,
                vertices[3].X, vertices[3].Y, vertices[3].Z, uv[3].X, uv[3].Y,
            };

            return TextureImpl.FromImage(image, ver);
        }
    }
}
