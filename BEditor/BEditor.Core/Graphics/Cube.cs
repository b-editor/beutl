using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Core.Graphics
{
    public class Cube : IDisposable
    {
        private readonly float[] vertices;

        public Cube(float width, float height, float depth, Color color, Material material)
        {
            Width = width;
            Height = height;
            Depth = depth;
            Color = color;
            Material = material;

            width /= 2;
            height /= 2;
            depth /= 2;

            vertices = new float[]
            {
                // Position
                -width, -height, -depth, // Front face
                 width, -height, -depth,
                 width,  height, -depth,
                 width,  height, -depth,
                -width,  height, -depth,
                -width, -height, -depth,

                -width, -height,  depth, // Back face
                 width, -height,  depth,
                 width,  height,  depth,
                 width,  height,  depth,
                -width,  height,  depth,
                -width, -height,  depth,

                -width,  height,  depth, // Left face
                -width,  height, -depth,
                -width, -height, -depth,
                -width, -height, -depth,
                -width, -height,  depth,
                -width,  height,  depth,

                 width,  height,  depth, // Right face
                 width,  height, -depth,
                 width, -height, -depth,
                 width, -height, -depth,
                 width, -height,  depth,
                 width,  height,  depth,

                -width, -height, -depth, // Bottom face
                 width, -height, -depth,
                 width, -height,  depth,
                 width, -height,  depth,
                -width, -height,  depth,
                -width, -height, -depth,

                -width,  height, -depth, // Top face
                 width,  height, -depth,
                 width,  height,  depth,
                 width,  height,  depth,
                -width,  height,  depth,
                -width,  height, -depth
            };

            VertexArrayObject = GL.GenVertexArray();
            GL.BindVertexArray(VertexArrayObject);

            VertexBufferObject = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, VertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
        }
        ~Cube()
        {
            if (!IsDisposed)
                Dispose();
        }

        public float Width { get; }
        public float Height { get; }
        public float Depth { get; }
        public Material Material { get; }
        public Color Color { get; }
        public ReadOnlyMemory<float> Vertices => vertices;
        public bool IsDisposed { get; private set; }
        public int VertexArrayObject { get; }
        public int VertexBufferObject { get; }

        public void Render()
        {
            GL.BindVertexArray(VertexBufferObject);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
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
