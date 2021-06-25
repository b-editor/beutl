using System;
using System.Numerics;

using BEditor.Graphics.Platform;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics.OpenGL
{
    public class LineImpl : GraphicsObject
    {
        private readonly float[] _vertices;

        public LineImpl(Vector3 start, Vector3 end, float width)
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

        public override float[] Vertices => _vertices;

        public Vector3 Start { get; }

        public Vector3 End { get; }

        public float Width { get; }

        public GraphicsHandle VertexBufferObject { get; }

        public GraphicsHandle VertexArrayObject { get; }

        public override void Draw()
        {
            GL.LineWidth(Width);

            GL.BindVertexArray(VertexArrayObject);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
        }

        protected override void Dispose(bool disposing)
        {
            GL.DeleteVertexArray(VertexArrayObject);
            GL.DeleteBuffer(VertexBufferObject);
        }
    }
}