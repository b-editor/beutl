using System.Numerics;

namespace Beutl.Graphics3D.Meshes;

public class MeshUtils
{
    public static void CreateLine(ref Mesh mesh, Vector3 point1, Vector3 point2, Vector3 color)
    {
        mesh.Vertices.Add(new Vertex(point1, color));
        mesh.Vertices.Add(new Vertex(point2, color));
    }

    public static void CreateArrowCap(ref Mesh mesh, Vector3 point, Vector3 up, Vector3 dir, float size1, float size2, Vector3 color)
    {
        dir = Vector3.Normalize(dir);
        up = Vector3.Normalize(up);
        Vector3 right = Vector3.Cross(up, dir);

        //CreateLine(ref mesh, point, dir * size2, color);
        CreateLine(ref mesh, point + up * size1, point + dir * size2, color);
        CreateLine(ref mesh, point + up * size1, point + right * size1, color);
        CreateLine(ref mesh, point + up * size1, point + -right * size1, color);
        CreateLine(ref mesh, point + -up * size1, point + dir * size2, color);
        CreateLine(ref mesh, point + -up * size1, point + right * size1, color);
        CreateLine(ref mesh, point + -up * size1, point + -right * size1, color);
        CreateLine(ref mesh, point + right * size1, point + dir * size2, color);
        CreateLine(ref mesh, point + -right * size1, point + dir * size2, color);
    }

}
