using System;
using System.Collections.Generic;
using System.Numerics;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics.OpenGL
{
    /// <summary>
    /// Represents an OpenGL ball.
    /// </summary>
    public class BallImpl : GraphicsObject
    {
        private const int Count = 8;
        private readonly float[] _vertices;

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

        public float RadiusX { get; }

        public float RadiusY { get; }

        public float RadiusZ { get; }

        public override float[] Vertices => _vertices;

        public GraphicsHandle VertexArrayObject { get; }

        public GraphicsHandle VertexBufferObject { get; }

        public override void Draw()
        {
            GL.BindVertexArray(VertexArrayObject);

            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, _vertices.Length / 3);
        }

        protected override void Dispose(bool disposing)
        {
            GL.DeleteBuffer(VertexBufferObject);
            GL.DeleteVertexArray(VertexArrayObject);
        }
    }
}