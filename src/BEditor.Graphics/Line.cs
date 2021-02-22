using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics
{
    public class Line : IGraphicsObject
    {
        private readonly float[] _vertices;
        private readonly SynchronizationContext? _synchronization;

        public Line(Vector3 start, Vector3 end, float width)
        {
            _synchronization = SynchronizationContext.Current;
            Debug.Assert(_synchronization is not null);

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
        }
        ~Line()
        {
            if (!IsDisposed) Dispose();
        }

        public ReadOnlyMemory<float> Vertices => _vertices;
        public Vector3 Start { get; }
        public Vector3 End { get; }
        public float Width { get; }
        public int VertexBufferObject { get; }
        public int VertexArrayObject { get; }
        public bool IsDisposed { get; private set; }

        public void Render()
        {
            GL.LineWidth(Width);

            GL.BindVertexArray(VertexArrayObject);
            GL.DrawArrays(PrimitiveType.Lines, 0, 2);
        }
        public void Dispose()
        {
            if (IsDisposed) return;

            _synchronization?.Post(state =>
            {
                var t = (Line)state!;
                GL.DeleteVertexArray(t.VertexArrayObject);
                GL.DeleteBuffer(t.VertexBufferObject);
            }, this);

            GC.SuppressFinalize(this);

            IsDisposed = true;
        }
    }
}
