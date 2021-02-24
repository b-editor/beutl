using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics
{
    public class Ball : IDisposable
    {
        private readonly float[] _vertices;
        private const int count = 8;

        public Ball(float radiusX,float radiusY,float radiusZ, Color color)
        {
            RadiusX = radiusX;
            RadiusY = radiusY;
            RadiusZ = radiusZ;
            Color = color;
            var a = (float)(Math.PI / count / 2);
            var b = (float)(Math.PI / count / 2);
            var verticesList = new List<float>();

            for (int k = -count + 1; k <= count; k++)
            {
                for (int i = 0; i <= count * 4; i++)
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
        }

        public float RadiusX { get; }
        public float RadiusY { get; }
        public float RadiusZ { get; }
        /// <summary>
        /// Get the color of this <see cref="Cube"/>.
        /// </summary>
        public Color Color { get; }
        /// <summary>
        /// Get the vertices of this <see cref="Cube"/>.
        /// </summary>
        public ReadOnlyMemory<float> Vertices => _vertices;
        /// <summary>
        /// Get whether an object has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }
        /// <summary>
        /// Get the VertexArray of this <see cref="Cube"/>.
        /// </summary>
        public int VertexArrayObject { get; }
        /// <summary>
        /// Get the VertexBuffer of this <see cref="Cube"/>.
        /// </summary>
        public int VertexBufferObject { get; }

        public void Render()
        {
            GL.BindVertexArray(VertexBufferObject);

            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, _vertices.Length / 3);
        }
        public void Dispose()
        {
            if (IsDisposed) return;


            GL.DeleteBuffer(VertexBufferObject);
            GL.DeleteVertexArray(VertexArrayObject);


            IsDisposed = true;
        }
    }
}
