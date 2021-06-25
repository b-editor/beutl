
using OpenTK.Graphics.OpenGL4;

namespace BEditor.Graphics.OpenGL
{
    public class CubeImpl : GraphicsObject
    {
        private readonly float[] _vertices;

        public CubeImpl(float width, float height, float depth)
        {
            Width = width;
            Height = height;
            Depth = depth;

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

        public float Width { get; }

        public float Height { get; }

        public float Depth { get; }

        public override float[] Vertices => _vertices;

        public GraphicsHandle VertexArrayObject { get; }

        public GraphicsHandle VertexBufferObject { get; }

        public override void Draw()
        {
            GL.BindVertexArray(VertexArrayObject);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
        }

        protected override void Dispose(bool disposing)
        {
            GL.DeleteBuffer(VertexBufferObject);
            GL.DeleteVertexArray(VertexArrayObject);
        }
    }
}