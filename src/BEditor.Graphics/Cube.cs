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
    public class Cube : GraphicsObject
    {
        private readonly float[] _vertices;

        /// <summary>
        /// Initializes a new instance of the <see cref="Cube"/> class.
        /// </summary>
        /// <param name="width">The width of the cube.</param>
        /// <param name="height">The height of the cube.</param>
        /// <param name="depth">The depth of the cube.</param>
        /// <param name="color">The color of the cube.</param>
        /// <exception cref="GraphicsException">OpenGL error occurred.</exception>
        public Cube(float width, float height, float depth, Color color)
            : this(width, height, depth, color, new(Color.Light, Color.Light, Color.Light, 16))
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Cube"/> class.
        /// </summary>
        /// <param name="width">The width of the cube.</param>
        /// <param name="height">The height of the cube.</param>
        /// <param name="depth">The depth of the cube.</param>
        /// <param name="color">The color of the cube.</param>
        /// <param name="material">The material of the cube.</param>
        /// <exception cref="GraphicsException">OpenGL error occurred.</exception>
        public Cube(float width, float height, float depth, Color color, Material material)
            : this(width, height, depth, color, material, Transform.Default)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Cube"/> class.
        /// </summary>
        /// <param name="width">The width of the cube.</param>
        /// <param name="height">The height of the cube.</param>
        /// <param name="depth">The depth of the cube.</param>
        /// <param name="color">The color of the cube.</param>
        /// <param name="material">The material of the cube.</param>
        /// <param name="transform">The transform of the cube.</param>
        /// <exception cref="GraphicsException">OpenGL error occurred.</exception>
        public Cube(float width, float height, float depth, Color color, Material material, Transform transform)
        {
            Width = width;
            Height = height;
            Depth = depth;
            Color = color;
            Material = material;
            Transform = transform;

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

            Tool.ThrowGLError();
        }

        /// <summary>
        /// Gets the width of this <see cref="Cube"/>.
        /// </summary>
        public float Width { get; }

        /// <summary>
        /// Gets the height of this <see cref="Cube"/>.
        /// </summary>
        public float Height { get; }

        /// <summary>
        /// Gets the depth of this <see cref="Cube"/>.
        /// </summary>
        public float Depth { get; }

        /// <inheritdoc/>
        public override ReadOnlyMemory<float> Vertices => _vertices;

        /// <summary>
        /// Gets the VertexArray of this <see cref="Cube"/>.
        /// </summary>
        public GraphicsHandle VertexArrayObject { get; }

        /// <summary>
        /// Gets the VertexBuffer of this <see cref="Cube"/>.
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