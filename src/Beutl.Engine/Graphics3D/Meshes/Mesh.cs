using System.Numerics;

namespace Beutl.Graphics3D.Meshes;

public class Mesh : CoreObject
{
    public int VertexArrayObject { get; private set; }

    public List<Vertex> Vertices { get; set; } = [];

    public int[]? Indices { get; set; }

    public BoundingBox Bounds { get; protected set; }

    public MeshResource CreateResource(Device device)
    {
        return new MeshResource(device, this);
    }

    public void Clear()
    {
        Vertices.Clear();
        Indices = null;
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

    protected BoundingBox GenerateAABB()
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
