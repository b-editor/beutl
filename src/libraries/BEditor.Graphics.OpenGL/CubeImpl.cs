// CubeImpl.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Drawing;
using BEditor.Graphics.Platform;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics.OpenGL
{
    /// <summary>
    /// Represents an OpenGL cube.
    /// </summary>
    public class CubeImpl : GraphicsObject, ICubeImpl
    {
        private readonly float[] _vertices;

        /// <summary>
        /// Initializes a new instance of the <see cref="CubeImpl"/> class.
        /// </summary>
        /// <param name="width">The width of the cube.</param>
        /// <param name="height">The height of the cube.</param>
        /// <param name="depth">The depth of the cube.</param>
        /// <exception cref="GraphicsException">OpenGL error occurred.</exception>
        public CubeImpl(float width, float height, float depth)
        {
            Width = width;
            Height = height;
            Depth = depth;

            width /= 2;
            height /= 2;
            depth /= 2;

#pragma warning disable SA1137
            _vertices = new float[]
            {
                // Position
                -width, -height, -depth,  0.0f,  0.0f, -1.0f, // Front face
                 width, -height, -depth,  0.0f,  0.0f, -1.0f,
                 width,  height, -depth,  0.0f,  0.0f, -1.0f,
                 width,  height, -depth,  0.0f,  0.0f, -1.0f,
                -width,  height, -depth,  0.0f,  0.0f, -1.0f,
                -width, -height, -depth,  0.0f,  0.0f, -1.0f,

                -width, -height,  depth,  0.0f,  0.0f,  1.0f, // Back face
                 width, -height,  depth,  0.0f,  0.0f,  1.0f,
                 width,  height,  depth,  0.0f,  0.0f,  1.0f,
                 width,  height,  depth,  0.0f,  0.0f,  1.0f,
                -width,  height,  depth,  0.0f,  0.0f,  1.0f,
                -width, -height,  depth,  0.0f,  0.0f,  1.0f,

                -width,  height,  depth, -1.0f,  0.0f,  0.0f, // Left face
                -width,  height, -depth, -1.0f,  0.0f,  0.0f,
                -width, -height, -depth, -1.0f,  0.0f,  0.0f,
                -width, -height, -depth, -1.0f,  0.0f,  0.0f,
                -width, -height,  depth, -1.0f,  0.0f,  0.0f,
                -width,  height,  depth, -1.0f,  0.0f,  0.0f,

                 width,  height,  depth,  1.0f,  0.0f,  0.0f, // Right face
                 width,  height, -depth,  1.0f,  0.0f,  0.0f,
                 width, -height, -depth,  1.0f,  0.0f,  0.0f,
                 width, -height, -depth,  1.0f,  0.0f,  0.0f,
                 width, -height,  depth,  1.0f,  0.0f,  0.0f,
                 width,  height,  depth,  1.0f,  0.0f,  0.0f,

                -width, -height, -depth,  0.0f, -1.0f,  0.0f, // Bottom face
                 width, -height, -depth,  0.0f, -1.0f,  0.0f,
                 width, -height,  depth,  0.0f, -1.0f,  0.0f,
                 width, -height,  depth,  0.0f, -1.0f,  0.0f,
                -width, -height,  depth,  0.0f, -1.0f,  0.0f,
                -width, -height, -depth,  0.0f, -1.0f,  0.0f,

                -width,  height, -depth,  0.0f,  1.0f,  0.0f, // Top face
                 width,  height, -depth,  0.0f,  1.0f,  0.0f,
                 width,  height,  depth,  0.0f,  1.0f,  0.0f,
                 width,  height,  depth,  0.0f,  1.0f,  0.0f,
                -width,  height,  depth,  0.0f,  1.0f,  0.0f,
                -width,  height, -depth,  0.0f,  1.0f,  0.0f,
            };
#pragma warning restore SA1137

            VertexArrayObject = GL.GenVertexArray();
            GL.BindVertexArray(VertexArrayObject);

            VertexBufferObject = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, VertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, _vertices.Length * sizeof(float), _vertices, BufferUsageHint.StaticDraw);

            Tool.ThrowGLError();
        }

        /// <inheritdoc/>
        public float Width { get; }

        /// <inheritdoc/>
        public float Height { get; }

        /// <inheritdoc/>
        public float Depth { get; }

        /// <inheritdoc/>
        public override ReadOnlyMemory<float> Vertices => _vertices;

        /// <summary>
        /// Gets the VertexArray of this <see cref="CubeImpl"/>.
        /// </summary>
        public GraphicsHandle VertexArrayObject { get; }

        /// <summary>
        /// Gets the VertexBuffer of this <see cref="CubeImpl"/>.
        /// </summary>
        public GraphicsHandle VertexBufferObject { get; }

        /// <inheritdoc/>
        public override void Draw()
        {
            GL.BindVertexArray(VertexArrayObject);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            GL.DeleteBuffer(VertexBufferObject);
            GL.DeleteVertexArray(VertexArrayObject);
        }
    }
}