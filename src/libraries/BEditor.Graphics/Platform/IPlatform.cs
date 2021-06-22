// IPlatform.cs
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
using BEditor.Graphics.Resources;

namespace BEditor.Graphics.Platform
{
    /// <summary>
    /// Represents the platform.
    /// </summary>
    public interface IPlatform
    {
        private static IPlatform? _current;

        /// <summary>
        /// Gets or sets the current platform.
        /// </summary>
        public static IPlatform Current
        {
            get => _current ?? throw new GraphicsException(Strings.PlatformIsNotSet);
            set => _current = value;
        }

        /// <summary>
        /// Creates the graphics context.
        /// </summary>
        /// <param name="width">The width of the graphics context.</param>
        /// <param name="height">The height of the graphics context.</param>
        /// <returns>Returns the implementation created by this method.</returns>
        public IGraphicsContextImpl CreateContext(int width, int height);

        /// <summary>
        /// Creates the ball.
        /// </summary>
        /// <param name="radiusX">The radius of the ball in the X-axis direction.</param>
        /// <param name="radiusY">The radius of the ball in the Y-axis direction.</param>
        /// <param name="radiusZ">The radius of the ball in the Z-axis direction.</param>
        /// <returns>Returns the implementation created by this method.</returns>
        public IBallImpl CreateBall(float radiusX, float radiusY, float radiusZ);

        /// <summary>
        /// Creates the cube.
        /// </summary>
        /// <param name="width">The width of the cube.</param>
        /// <param name="height">The height of the cube.</param>
        /// <param name="depth">The depth of the cube.</param>
        /// <returns>Returns the implementation created by this method.</returns>
        public ICubeImpl CreateCube(float width, float height, float depth);

        /// <summary>
        /// Creates the line.
        /// </summary>
        /// <param name="start">The start position of the line.</param>
        /// <param name="end">The end position of the line.</param>
        /// <param name="width">The width of the line.</param>
        /// <returns>Returns the implementation created by this method.</returns>
        public ILineImpl CreateLine(Vector3 start, Vector3 end, float width);

        /// <summary>
        /// Creates the texture.
        /// </summary>
        /// <param name="image">The image to create texture.</param>
        /// <param name="vertices">The vertices.</param>
        /// <returns>Returns the implementation created by this method.</returns>
        public ITextureImpl CreateTexture(Image<BGRA32> image, VertexPositionTexture[]? vertices = null);
    }
}