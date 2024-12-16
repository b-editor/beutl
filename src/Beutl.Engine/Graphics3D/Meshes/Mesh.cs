using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;

namespace Beutl.Graphics3D.Meshes;

public class Mesh : CoreObject
{
    public int VertexArrayObject { get; private set; }

    public List<Vertex> Vertices { get; set; } = [];

    public int[]? Indices { get; set; }

    public BoundingBox Bounds { get; private set; }

    public void GenerateVAO()
    {
        Vertex[] vertices = [.. Vertices];

        VertexArrayObject = GL.GenVertexArray();

        int vbo = GL.GenBuffer();

        GL.BindVertexArray(VertexArrayObject);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

        int sizeOfVertex = Marshal.SizeOf(vertices[0]);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeOfVertex, vertices, BufferUsage.StaticDraw);

        //Position
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeOfVertex, IntPtr.Zero);
        GL.EnableVertexAttribArray(0);
        //Color
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, sizeOfVertex, Marshal.SizeOf(vertices[0].Position));
        GL.EnableVertexAttribArray(1);
        //Normal
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, sizeOfVertex, Marshal.SizeOf(vertices[0].Position) + Marshal.SizeOf(vertices[0].Color));
        GL.EnableVertexAttribArray(2);
        //TexCoord
        GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, sizeOfVertex, Marshal.SizeOf(vertices[0].Position) + Marshal.SizeOf(vertices[0].Color) + Marshal.SizeOf(vertices[0].Normal));
        GL.EnableVertexAttribArray(3);
        //Tangent
        GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, false, sizeOfVertex, Marshal.SizeOf(vertices[0].Position) + Marshal.SizeOf(vertices[0].Color) + Marshal.SizeOf(vertices[0].Normal) + Marshal.SizeOf(vertices[0].TexCoord));
        GL.EnableVertexAttribArray(4);
        //Bitangent
        GL.VertexAttribPointer(5, 3, VertexAttribPointerType.Float, false, sizeOfVertex, Marshal.SizeOf(vertices[0].Position) + Marshal.SizeOf(vertices[0].Color) + Marshal.SizeOf(vertices[0].Normal) + Marshal.SizeOf(vertices[0].TexCoord) + Marshal.SizeOf(vertices[0].Tangent));
        GL.EnableVertexAttribArray(5);

        int ebo = 0;
        if (Indices != null)
        {
            int[] indices = [.. Indices];
            ebo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsage.StaticDraw);
        }


        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.DeleteBuffer(vbo);
        if (Indices != null)
        {
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
            GL.DeleteBuffer(ebo);
        }

        Bounds = GenerateAABB();
    }

    public void Clear()
    {
        Vertices.Clear();
        Indices = null;

        if (VertexArrayObject > 0)
            GL.DeleteVertexArray(VertexArrayObject);
    }

    public void AddFace(Vertex v1, Vertex v2, Vertex v3, Vertex v4)
    {
        Vertices.Add(v1);
        Vertices.Add(v2);
        Vertices.Add(v3);
        Vertices.Add(v2);
        Vertices.Add(v4);
        Vertices.Add(v3);
    }

    public void AddFace(ref Vertex v1, ref Vertex v2, ref Vertex v3, ref Vertex v4, ref Vector3 n)
    {
        if (n == Vector3.Zero)
            n = Vector3.Cross(v1.Position - v2.Position, v2.Position - v3.Position);

        v1 = v1.WithNormal(n);
        v2 = v2.WithNormal(n);
        v3 = v3.WithNormal(n);
        v4 = v4.WithNormal(n);

        AddFace(v1, v2, v3, v4);
    }

    private BoundingBox GenerateAABB()
    {
        var bbox = new BoundingBox();

        foreach (Vertex v in Vertices)
        {
            bbox = BoundingBox.Combine(bbox, v.Position);
        }

        return bbox;
    }

    public void Scale(float scale)
    {
        for (int i = 0; i < Vertices.Count; ++i)
        {
            Vertex v = Vertices[i];
            v = v.WithPosition(v.Position * scale);
            Vertices[i] = v;
        }
    }
}
