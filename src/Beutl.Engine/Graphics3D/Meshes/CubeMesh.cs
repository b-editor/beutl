using System.Numerics;

namespace Beutl.Graphics3D.Meshes;

public class CubeMesh : Mesh
{
    public CubeMesh()
    {
        var v1 = new Vertex(new Vector3(-0.5f, 0.5f, -0.5f), Vector3.One, Vector3.Zero, new Vector2(0, 0));
        var v2 = new Vertex(new Vector3(0.5f, 0.5f, -0.5f), Vector3.One, Vector3.Zero, new Vector2(1, 0));
        var v3 = new Vertex(new Vector3(-0.5f, -0.5f, -0.5f), Vector3.One, Vector3.Zero, new Vector2(0, 1));
        var v4 = new Vertex(new Vector3(0.5f, -0.5f, -0.5f), Vector3.One, Vector3.Zero, new Vector2(1, 1));

        var v5 = new Vertex(new Vector3(-0.5f, 0.5f, 0.5f), Vector3.One, Vector3.Zero, new Vector2(0, 0));
        var v6 = new Vertex(new Vector3(0.5f, 0.5f, 0.5f), Vector3.One, Vector3.Zero, new Vector2(1, 0));
        var v7 = new Vertex(new Vector3(-0.5f, -0.5f, 0.5f), Vector3.One, Vector3.Zero, new Vector2(0, 1));
        var v8 = new Vertex(new Vector3(0.5f, -0.5f, 0.5f), Vector3.One, Vector3.Zero, new Vector2(1, 1));

        v1 = v1.WithColor(new Vector3(1, 0, 0));
        v2 = v2.WithColor(new Vector3(0, 1, 0));
        v3 = v3.WithColor(new Vector3(0, 0, 1));
        v4 = v4.WithColor(new Vector3(0, 1, 1));
        v5 = v5.WithColor(new Vector3(1, 0, 1));
        v6 = v6.WithColor(new Vector3(1, 1, 0));
        v7 = v7.WithColor(new Vector3(1, 1, 1));
        v8 = v8.WithColor(new Vector3(0, 0, 0));

        AddFace(v1, v2, v3, v4);
        AddFace(v2, v6, v4, v8);
        AddFace(v6, v5, v8, v7);
        AddFace(v5, v1, v7, v3);
        AddFace(v5, v6, v1, v2);
        AddFace(v3, v4, v7, v8);

        GenerateVAO();
    }
}
