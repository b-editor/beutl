// BallImpl.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Numerics;

using BEditor.Drawing;
using BEditor.Graphics.Platform;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics.OpenGL
{
    /// <summary>
    /// Represents an OpenGL ball.
    /// </summary>
    public class BallImpl : GraphicsObject, IBallImpl
    {
        private const int Count = 8;
        private readonly float[] _vertices;

        /// <summary>
        /// Initializes a new instance of the <see cref="BallImpl"/> class.
        /// </summary>
        /// <param name="radiusX">The radius of the ball in the X-axis direction.</param>
        /// <param name="radiusY">The radius of the ball in the Y-axis direction.</param>
        /// <param name="radiusZ">The radius of the ball in the Z-axis direction.</param>
        /// <exception cref="GraphicsException">OpenGL error occurred.</exception>
        public BallImpl(float radiusX, float radiusY, float radiusZ)
        {
            RadiusX = radiusX;
            RadiusY = radiusY;
            RadiusZ = radiusZ;

            const float a = (float)(Math.PI / Count / 2);
            const float b = (float)(Math.PI / Count / 2);
            var verticesList = new List<float>();

            for (var k = -Count + 1; k <= Count; k++)
            {
                for (var i = 0; i <= Count * 4; i++)
                {
                    var vec1 = new Vector3(
                        radiusX * MathF.Cos(b * k) * MathF.Cos(a * i),
                        radiusY * MathF.Cos(b * k) * MathF.Sin(a * i),
                        radiusZ * MathF.Sin(b * k));
                    verticesList.Add(vec1.X);
                    verticesList.Add(vec1.Y);
                    verticesList.Add(vec1.Z);

                    var vec2 = new Vector3(
                        radiusX * MathF.Cos(b * (k - 1)) * MathF.Cos(a * i),
                        radiusY * MathF.Cos(b * (k - 1)) * MathF.Sin(a * i),
                        radiusZ * MathF.Sin(b * (k - 1)));
                    verticesList.Add(vec2.X);
                    verticesList.Add(vec2.Y);
                    verticesList.Add(vec2.Z);
                }
            }

            _vertices = verticesList.ToArray();

            VertexArrayObject = GL.GenVertexArray();
            GL.BindVertexArray(VertexArrayObject);

            VertexBufferObject = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, VertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, _vertices.Length * sizeof(float), _vertices, BufferUsageHint.StaticDraw);

            Tool.ThrowGLError();
        }

        /// <inheritdoc/>
        public float RadiusX { get; }

        /// <inheritdoc/>
        public float RadiusY { get; }

        /// <inheritdoc/>
        public float RadiusZ { get; }

        /// <inheritdoc/>
        public override ReadOnlyMemory<float> Vertices => _vertices;

        /// <summary>
        /// Gets the VertexArray of this <see cref="BallImpl"/>.
        /// </summary>
        public GraphicsHandle VertexArrayObject { get; }

        /// <summary>
        /// Gets the VertexBuffer of this <see cref="BallImpl"/>.
        /// </summary>
        public GraphicsHandle VertexBufferObject { get; }

        /// <inheritdoc/>
        public override void Draw()
        {
            GL.BindVertexArray(VertexArrayObject);

            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, _vertices.Length / 3);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            GL.DeleteBuffer(VertexBufferObject);
            GL.DeleteVertexArray(VertexArrayObject);
        }
    }
}