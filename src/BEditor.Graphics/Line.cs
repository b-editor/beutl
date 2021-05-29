// Line.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Numerics;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents an OpenGL line.
    /// </summary>
    public class Line : GraphicsObject
    {
        private readonly float[] _vertices;

        /// <summary>
        /// Initializes a new instance of the <see cref="Line"/> class.
        /// </summary>
        /// <param name="start">The start position of the line.</param>
        /// <param name="end">The end position of the line.</param>
        /// <param name="width">The width of the line.</param>
        /// <exception cref="GraphicsException">OpenGL error occurred.</exception>
        public Line(Vector3 start, Vector3 end, float width)
        {
            Start = start;
            End = end;
            Width = width;

            _vertices = new float[]
            {
                start.X, start.Y, start.Z,
                end.X, end.Y, end.Z,
            };

            VertexArrayObject = GL.GenVertexArray();
            VertexBufferObject = GL.GenBuffer();
            GL.BindVertexArray(VertexArrayObject);

            GL.BindBuffer(BufferTarget.ArrayBuffer, VertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, _vertices.Length * sizeof(float), _vertices, BufferUsageHint.StaticDraw);

            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 3 * sizeof(float), IntPtr.Zero);
            GL.EnableVertexAttribArray(0);

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);

            Tool.ThrowGLError();
        }

        /// <inheritdoc/>
        public override ReadOnlyMemory<float> Vertices => _vertices;

        /// <summary>
        /// Gets the start position of this <see cref="Line"/>.
        /// </summary>
        public Vector3 Start { get; }

        /// <summary>
        /// Gets the end position of this <see cref="Line"/>.
        /// </summary>
        public Vector3 End { get; }

        /// <summary>
        /// Gets the width of this <see cref="Line"/>.
        /// </summary>
        public float Width { get; }

        /// <summary>
        /// Gets the VertexBuffer of this <see cref="Line"/>.
        /// </summary>
        public GraphicsHandle VertexBufferObject { get; }

        /// <summary>
        /// Gets the VertexArray of this <see cref="Line"/>.
        /// </summary>
        public GraphicsHandle VertexArrayObject { get; }

        /// <inheritdoc/>
        public override void Draw()
        {
            GL.LineWidth(Width);

            GL.BindVertexArray(VertexArrayObject);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            GL.DeleteVertexArray(VertexArrayObject);
            GL.DeleteBuffer(VertexBufferObject);
        }
    }
}