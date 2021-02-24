using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;

using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics
{
    /// <summary>
    /// Represents an OpenGL cube.
    /// </summary>
    public class Cube : IDisposable
    {
        private readonly float[] _vertices;

        /// <summary>
        /// Initializes a new instance of the <see cref="Cube"/> class.
        /// </summary>
        /// <param name="width">Width of cube</param>
        /// <param name="height">Height of cube</param>
        /// <param name="depth">Depth of cube</param>
        /// <param name="color">Color of cube</param>
        /// <param name="material">Material of cube</param>
        public Cube(float width, float height, float depth, Color color)
        {
            Width = width;
            Height = height;
            Depth = depth;
            Color = color;
            Material = new(Color.Light, Color.Light, Color.Light, 16);

            width /= 2;
            height /= 2;
            depth /= 2;

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

            VertexArrayObject = GL.GenVertexArray();
            GL.BindVertexArray(VertexArrayObject);

            VertexBufferObject = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, VertexBufferObject);
            GL.BufferData(BufferTarget.ArrayBuffer, _vertices.Length * sizeof(float), _vertices, BufferUsageHint.StaticDraw);
        }
        /// <summary>
        /// Discards the reference to the target represented by the current <see cref="Cube"/> object.
        /// </summary>
        ~Cube()
        {
            if (!IsDisposed)
                Dispose();
        }

        /// <summary>
        /// Get the width of this <see cref="Cube"/>.
        /// </summary>
        public float Width { get; }
        /// <summary>
        /// Get the height of this <see cref="Cube"/>.
        /// </summary>
        public float Height { get; }
        /// <summary>
        /// Get the depth of this <see cref="Cube"/>.
        /// </summary>
        public float Depth { get; }
        /// <summary>
        /// Get the material of this <see cref="Cube"/>.
        /// </summary>
        public Material Material { get; }
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

        /// <summary>
        /// Render this <see cref="Cube"/>.
        /// </summary>
        public void Render()
        {
            GL.BindVertexArray(VertexBufferObject);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
        }
        /// <inheritdoc/>
        public void Dispose()
        {
            if (IsDisposed) return;


            GL.DeleteBuffer(VertexBufferObject);
            GL.DeleteVertexArray(VertexArrayObject);

            IsDisposed = true;
        }
    }
}
