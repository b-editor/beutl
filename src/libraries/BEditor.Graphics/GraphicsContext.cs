// GraphicsContext.cs
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

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents the graphics context.
    /// </summary>
    public sealed class GraphicsContext : IDisposable
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GraphicsContext"/> class.
        /// </summary>
        /// <param name="width">The width of the graphics context.</param>
        /// <param name="height">The height of the graphics context.</param>
        /// <exception cref="GraphicsException">Platform is not set.</exception>
        public GraphicsContext(int width, int height)
            : this(IPlatform.Current.CreateContext(width, height))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GraphicsContext"/> class.
        /// </summary>
        /// <param name="impl">The graphics context implementation.</param>
        public GraphicsContext(IGraphicsContextImpl impl)
        {
            PlatformImpl = impl;
        }

        /// <summary>
        /// Gets the width of the frame buffer.
        /// </summary>
        public int Width => PlatformImpl.Width;

        /// <summary>
        /// Gets the height of the frame buffer.
        /// </summary>
        public int Height => PlatformImpl.Height;

        /// <summary>
        /// Gets a value indicating whether this instance has been disposed.
        /// </summary>
        public bool IsDisposed => PlatformImpl.IsDisposed;

        /// <summary>
        /// Gets or sets the camera.
        /// </summary>
        public Camera Camera
        {
            get => PlatformImpl.Camera;
            set => PlatformImpl.Camera = value;
        }

        /// <summary>
        /// Gets or sets the light.
        /// </summary>
        public Light? Light
        {
            get => PlatformImpl.Light;
            set => PlatformImpl.Light = value;
        }

        /// <summary>
        /// Gets or sets the depth stencil state.
        /// </summary>
        public DepthStencilState DepthStencilState
        {
            get => PlatformImpl.DepthStencilState;
            set => PlatformImpl.DepthStencilState = value;
        }

        /// <summary>
        /// Gets the graphics context implementation.
        /// </summary>
        public IGraphicsContextImpl PlatformImpl { get; }

        /// <summary>
        /// Sets the framebuffer size.
        /// </summary>
        /// <param name="size">The framebuffer size.</param>
        public void SetSize(Size size)
        {
            PlatformImpl.SetSize(size);
        }

        /// <summary>
        /// Clears the framebuffer.
        /// </summary>
        public void Clear()
        {
            PlatformImpl.Clear();
        }

        /// <summary>
        /// Draws the texture into the frame buffer.
        /// </summary>
        /// <param name="texture">The texture to be drawn.</param>
        public void DrawTexture(Texture texture)
        {
            PlatformImpl.DrawTexture(texture);
        }

        /// <summary>
        /// Draws the cube into the frame buffer.
        /// </summary>
        /// <param name="cube">The cube to be drawn.</param>
        public void DrawCube(Cube cube)
        {
            PlatformImpl.DrawCube(cube);
        }

        /// <summary>
        /// Draws the ball into the frame buffer.
        /// </summary>
        /// <param name="ball">The ball to be drawn.</param>
        public void DrawBall(Ball ball)
        {
            PlatformImpl.DrawBall(ball);
        }

        /// <summary>
        /// Draws the line into the frame buffer.
        /// </summary>
        /// <param name="start">The starting coordinates of the line.</param>
        /// <param name="end">The ending coordinates of the line.</param>
        /// <param name="width">The width of the line.</param>
        /// <param name="transform">The transformation matrix for drawing the line.</param>
        /// <param name="color">The color of the line.</param>
        /// <exception cref="GraphicsException">Platform is not set.</exception>
        public void DrawLine(Vector3 start, Vector3 end, float width, Transform transform, Color color)
        {
            using var line = new Line(start, end, width)
            {
                Transform = transform,
                Color = color,
            };
            DrawLine(line);
        }

        /// <summary>
        /// Draws the line into the frame buffer.
        /// </summary>
        /// <param name="line">The line to be drawn.</param>
        public void DrawLine(Line line)
        {
            PlatformImpl.DrawLine(line);
        }

        /// <summary>
        /// Reads an image.
        /// </summary>
        /// <param name="image">The image to write the frame buffer pixels.</param>
        public void ReadImage(Image<BGRA32> image)
        {
            PlatformImpl.ReadImage(image);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!IsDisposed)
            {
                PlatformImpl.Dispose();
            }
        }
    }
}