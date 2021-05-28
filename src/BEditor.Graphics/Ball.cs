// Ball.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Numerics;

using BEditor.Drawing;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents an OpenGL ball.
    /// </summary>
    public class Ball : GraphicsObject
    {
        private const int Count = 8;
        private readonly float[] _vertices;

        /// <summary>
        /// Initializes a new instance of the <see cref="Ball"/> class.
        /// </summary>
        /// <param name="radiusX">The radius of the ball in the X-axis direction.</param>
        /// <param name="radiusY">The radius of the ball in the Y-axis direction.</param>
        /// <param name="radiusZ">The radius of the ball in the Z-axis direction.</param>
        /// <param name="color">The color of the ball.</param>
        /// <exception cref="GraphicsException">OpenGL error occurred.</exception>
        public Ball(float radiusX, float radiusY, float radiusZ, Color color)
            : this(radiusX, radiusY, radiusZ, color, new(Colors.White, Colors.White, Colors.White, 16))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Ball"/> class.
        /// </summary>
        /// <param name="radiusX">The radius of the ball in the X-axis direction.</param>
        /// <param name="radiusY">The radius of the ball in the Y-axis direction.</param>
        /// <param name="radiusZ">The radius of the ball in the Z-axis direction.</param>
        /// <param name="color">The color of the ball.</param>
        /// <param name="material">The material of the ball.</param>
        /// <exception cref="GraphicsException">OpenGL error occurred.</exception>
        public Ball(float radiusX, float radiusY, float radiusZ, Color color, Material material)
            : this(radiusX, radiusY, radiusZ, color, material, Transform.Default)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Ball"/> class.
        /// </summary>
        /// <param name="radiusX">The radius of the ball in the X-axis direction.</param>
        /// <param name="radiusY">The radius of the ball in the Y-axis direction.</param>
        /// <param name="radiusZ">The radius of the ball in the Z-axis direction.</param>
        /// <param name="color">The color of the ball.</param>
        /// <param name="material">The material of the ball.</param>
        /// <param name="transform">The transform of the ball.</param>
        /// <exception cref="GraphicsException">OpenGL error occurred.</exception>
        public Ball(float radiusX, float radiusY, float radiusZ, Color color, Material material, Transform transform)
        {
            RadiusX = radiusX;
            RadiusY = radiusY;
            RadiusZ = radiusZ;
            Color = color;
            Material = material;
            Transform = transform;

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

        /// <summary>
        /// Gets the radius of this <see cref="Ball"/> in the X-axis direction.
        /// </summary>
        public float RadiusX { get; }

        /// <summary>
        /// Gets the radius of this <see cref="Ball"/> in the Y-axis direction.
        /// </summary>
        public float RadiusY { get; }

        /// <summary>
        /// Gets the radius of this <see cref="Ball"/> in the Z-axis direction.
        /// </summary>
        public float RadiusZ { get; }

        /// <inheritdoc/>
        public override ReadOnlyMemory<float> Vertices => _vertices;

        /// <summary>
        /// Gets the VertexArray of this <see cref="Ball"/>.
        /// </summary>
        public GraphicsHandle VertexArrayObject { get; }

        /// <summary>
        /// Gets the VertexBuffer of this <see cref="Ball"/>.
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