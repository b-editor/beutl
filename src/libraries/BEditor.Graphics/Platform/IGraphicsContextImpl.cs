// IGraphicsContextImpl.cs
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

namespace BEditor.Graphics.Platform
{
    /// <summary>
    /// Defines a graphics context implementation.
    /// </summary>
    public interface IGraphicsContextImpl : IDisposable
    {
        /// <summary>
        /// Gets the width of the frame buffer.
        /// </summary>
        public int Width { get; }

        /// <summary>
        /// Gets the height of the frame buffer.
        /// </summary>
        public int Height { get; }

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed { get; }

        /// <summary>
        /// Gets or sets the camera.
        /// </summary>
        public Camera Camera { get; set; }

        /// <summary>
        /// Gets or sets the light.
        /// </summary>
        public Light? Light { get; set; }

        /// <summary>
        /// Gets or sets the depth stencil state.
        /// </summary>
        public DepthStencilState DepthStencilState { get; set; }

        /// <summary>
        /// Makes current.
        /// </summary>
        public void MakeCurrent();

        /// <summary>
        /// Sets the framebuffer size.
        /// </summary>
        /// <param name="size">The framebuffer size.</param>
        public void SetSize(Size size);

        /// <summary>
        /// Clears the framebuffer.
        /// </summary>
        public void Clear();

        /// <summary>
        /// Draws the texture into the frame buffer.
        /// </summary>
        /// <param name="texture">The texture to be drawn.</param>
        public void DrawTexture(Texture texture);

        /// <summary>
        /// Draws the cube into the frame buffer.
        /// </summary>
        /// <param name="cube">The cube to be drawn.</param>
        public void DrawCube(Cube cube);

        /// <summary>
        /// Draws the ball into the frame buffer.
        /// </summary>
        /// <param name="ball">The ball to be drawn.</param>
        public void DrawBall(Ball ball);

        /// <summary>
        /// Draws the line into the frame buffer.
        /// </summary>
        /// <param name="line">The line to be drawn.</param>
        public void DrawLine(Line line);

        /// <summary>
        /// Reads an image.
        /// </summary>
        /// <param name="image">The image to write the frame buffer pixels.</param>
        public void ReadImage(Image<BGRA32> image);
    }
}