// SkiaPlatform.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Numerics;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics.Platform;

namespace BEditor.Graphics.Skia
{
    /// <summary>
    /// Represents the skia platform.
    /// </summary>
    public sealed class SkiaPlatform : IPlatform
    {
        /// <inheritdoc/>
        public IBallImpl CreateBall(float radiusX, float radiusY, float radiusZ)
        {
            return new MockBallImpl();
        }

        /// <inheritdoc/>
        public IGraphicsContextImpl CreateContext(int width, int height)
        {
            return new GraphicsContextImpl(width, height);
        }

        /// <inheritdoc/>
        public ICubeImpl CreateCube(float width, float height, float depth)
        {
            return new MockCubeImpl();
        }

        /// <inheritdoc/>
        public ILineImpl CreateLine(Vector3 start, Vector3 end, float width)
        {
            return new LineImpl(start, end, width);
        }

        /// <inheritdoc/>
        public ITextureImpl CreateTexture(Image<BGRA32> image, VertexPositionTexture[]? vertices = null)
        {
            return new TextureImpl(image);
        }
    }
}
