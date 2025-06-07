using System.Numerics;

namespace Beutl.Graphics.Rendering.OpenGL;

/// <summary>
/// 基本的な3Dメッシュの実装
/// </summary>
public class BasicMesh : I3DMesh
{
    public Vector3[] VerticesArray { get; }
    public Vector3[] NormalsArray { get; }
    public Vector2[] TexCoordsArray { get; }
    public uint[] IndicesArray { get; }

    public ReadOnlySpan<Vector3> Vertices => VerticesArray;
    public ReadOnlySpan<Vector3> Normals => NormalsArray;
    public ReadOnlySpan<Vector2> TexCoords => TexCoordsArray;
    public ReadOnlySpan<uint> Indices => IndicesArray;

    public BasicMesh(Vector3[] vertices, Vector3[] normals, Vector2[] texCoords, uint[] indices)
    {
        VerticesArray = vertices;
        NormalsArray = normals;
        TexCoordsArray = texCoords;
        IndicesArray = indices;
    }

    /// <summary>
    /// 立方体メッシュを作成
    /// </summary>
    public static BasicMesh CreateCube(float size = 1.0f)
    {
        float s = size * 0.5f;

        Vector3[] vertices = [
            // 前面
            new(-s, -s,  s), new( s, -s,  s), new( s,  s,  s), new(-s,  s,  s),
            // 背面
            new(-s, -s, -s), new(-s,  s, -s), new( s,  s, -s), new( s, -s, -s),
            // 左面
            new(-s, -s, -s), new(-s, -s,  s), new(-s,  s,  s), new(-s,  s, -s),
            // 右面
            new( s, -s, -s), new( s,  s, -s), new( s,  s,  s), new( s, -s,  s),
            // 上面
            new(-s,  s, -s), new(-s,  s,  s), new( s,  s,  s), new( s,  s, -s),
            // 下面
            new(-s, -s, -s), new( s, -s, -s), new( s, -s,  s), new(-s, -s,  s)
        ];

        Vector3[] normals = [
            // 前面
            new(0, 0, 1), new(0, 0, 1), new(0, 0, 1), new(0, 0, 1),
            // 背面
            new(0, 0, -1), new(0, 0, -1), new(0, 0, -1), new(0, 0, -1),
            // 左面
            new(-1, 0, 0), new(-1, 0, 0), new(-1, 0, 0), new(-1, 0, 0),
            // 右面
            new(1, 0, 0), new(1, 0, 0), new(1, 0, 0), new(1, 0, 0),
            // 上面
            new(0, 1, 0), new(0, 1, 0), new(0, 1, 0), new(0, 1, 0),
            // 下面
            new(0, -1, 0), new(0, -1, 0), new(0, -1, 0), new(0, -1, 0)
        ];

        Vector2[] texCoords = [
            // 前面
            new(0, 0), new(1, 0), new(1, 1), new(0, 1),
            // 背面
            new(1, 0), new(1, 1), new(0, 1), new(0, 0),
            // 左面
            new(1, 0), new(0, 0), new(0, 1), new(1, 1),
            // 右面
            new(1, 0), new(1, 1), new(0, 1), new(0, 0),
            // 上面
            new(0, 1), new(0, 0), new(1, 0), new(1, 1),
            // 下面
            new(1, 1), new(0, 1), new(0, 0), new(1, 0)
        ];

        uint[] indices = [
            // 前面
            0, 1, 2, 2, 3, 0,
            // 背面
            4, 5, 6, 6, 7, 4,
            // 左面
            8, 9, 10, 10, 11, 8,
            // 右面
            12, 13, 14, 14, 15, 12,
            // 上面
            16, 17, 18, 18, 19, 16,
            // 下面
            20, 21, 22, 22, 23, 20
        ];

        return new BasicMesh(vertices, normals, texCoords, indices);
    }

    /// <summary>
    /// 球体メッシュを作成
    /// </summary>
    public static BasicMesh CreateSphere(float radius = 1.0f, int segments = 32, int rings = 16)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var texCoords = new List<Vector2>();
        var indices = new List<uint>();

        // 頂点を生成
        for (int ring = 0; ring <= rings; ring++)
        {
            float phi = MathF.PI * ring / rings;
            float sinPhi = MathF.Sin(phi);
            float cosPhi = MathF.Cos(phi);

            for (int segment = 0; segment <= segments; segment++)
            {
                float theta = 2.0f * MathF.PI * segment / segments;
                float sinTheta = MathF.Sin(theta);
                float cosTheta = MathF.Cos(theta);

                Vector3 position = new(
                    radius * sinPhi * cosTheta,
                    radius * cosPhi,
                    radius * sinPhi * sinTheta
                );

                Vector3 normal = Vector3.Normalize(position);
                Vector2 texCoord = new((float)segment / segments, (float)ring / rings);

                vertices.Add(position);
                normals.Add(normal);
                texCoords.Add(texCoord);
            }
        }

        // インデックスを生成
        for (int ring = 0; ring < rings; ring++)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                uint current = (uint)(ring * (segments + 1) + segment);
                uint next = current + 1;
                uint below = (uint)((ring + 1) * (segments + 1) + segment);
                uint belowNext = below + 1;

                // 上の三角形
                indices.Add(current);
                indices.Add(below);
                indices.Add(next);

                // 下の三角形
                indices.Add(next);
                indices.Add(below);
                indices.Add(belowNext);
            }
        }

        return new BasicMesh(vertices.ToArray(), normals.ToArray(), texCoords.ToArray(), indices.ToArray());
    }

    /// <summary>
    /// 平面メッシュを作成
    /// </summary>
    public static BasicMesh CreatePlane(Vector2 size, int subdivisions = 1)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var texCoords = new List<Vector2>();
        var indices = new List<uint>();

        float halfWidth = size.X * 0.5f;
        float halfHeight = size.Y * 0.5f;

        // 頂点を生成
        for (int y = 0; y <= subdivisions; y++)
        {
            for (int x = 0; x <= subdivisions; x++)
            {
                float xPos = -halfWidth + (size.X * x) / subdivisions;
                float zPos = -halfHeight + (size.Y * y) / subdivisions;

                vertices.Add(new Vector3(xPos, 0, zPos));
                normals.Add(new Vector3(0, 1, 0));
                texCoords.Add(new Vector2((float)x / subdivisions, (float)y / subdivisions));
            }
        }

        // インデックスを生成
        for (int y = 0; y < subdivisions; y++)
        {
            for (int x = 0; x < subdivisions; x++)
            {
                uint topLeft = (uint)(y * (subdivisions + 1) + x);
                uint topRight = topLeft + 1;
                uint bottomLeft = (uint)((y + 1) * (subdivisions + 1) + x);
                uint bottomRight = bottomLeft + 1;

                // 上の三角形
                indices.Add(topLeft);
                indices.Add(bottomLeft);
                indices.Add(topRight);

                // 下の三角形
                indices.Add(topRight);
                indices.Add(bottomLeft);
                indices.Add(bottomRight);
            }
        }

        return new BasicMesh(vertices.ToArray(), normals.ToArray(), texCoords.ToArray(), indices.ToArray());
    }
}
