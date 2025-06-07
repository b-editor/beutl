using System.Numerics;

namespace Beutl.Graphics.Rendering;

/// <summary>
/// 基本的な3Dメッシュ実装
/// </summary>
public class BasicMesh : I3DMesh
{
    private readonly Vector3[] _vertices;
    private readonly Vector3[] _normals;
    private readonly Vector2[] _texCoords;
    private readonly uint[] _indices;

    public BasicMesh(
        Vector3[] vertices,
        Vector3[] normals,
        Vector2[] texCoords,
        uint[] indices)
    {
        _vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
        _normals = normals ?? throw new ArgumentNullException(nameof(normals));
        _texCoords = texCoords ?? throw new ArgumentNullException(nameof(texCoords));
        _indices = indices ?? throw new ArgumentNullException(nameof(indices));
    }

    public ReadOnlySpan<Vector3> Vertices => _vertices;
    public ReadOnlySpan<Vector3> Normals => _normals;
    public ReadOnlySpan<Vector2> TexCoords => _texCoords;
    public ReadOnlySpan<uint> Indices => _indices;

    /// <summary>
    /// 立方体メッシュを作成
    /// </summary>
    public static BasicMesh CreateCube(float size = 1.0f)
    {
        float half = size * 0.5f;

        Vector3[] vertices = {
            // Front face
            new Vector3(-half, -half,  half), // 0
            new Vector3( half, -half,  half), // 1
            new Vector3( half,  half,  half), // 2
            new Vector3(-half,  half,  half), // 3
            
            // Back face
            new Vector3(-half, -half, -half), // 4
            new Vector3( half, -half, -half), // 5
            new Vector3( half,  half, -half), // 6
            new Vector3(-half,  half, -half), // 7
        };

        Vector3[] normals = {
            // Front face
            new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1), new Vector3(0, 0, 1),
            // Back face
            new Vector3(0, 0, -1), new Vector3(0, 0, -1), new Vector3(0, 0, -1), new Vector3(0, 0, -1),
        };

        Vector2[] texCoords = {
            // Front face
            new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0),
            // Back face
            new Vector2(1, 1), new Vector2(0, 1), new Vector2(0, 0), new Vector2(1, 0),
        };

        uint[] indices = {
            // Front face
            0, 1, 2, 2, 3, 0,
            // Back face
            4, 6, 5, 6, 4, 7,
            // Left face
            4, 0, 3, 3, 7, 4,
            // Right face
            1, 5, 6, 6, 2, 1,
            // Top face
            3, 2, 6, 6, 7, 3,
            // Bottom face
            4, 1, 0, 1, 4, 5
        };

        return new BasicMesh(vertices, normals, texCoords, indices);
    }

    /// <summary>
    /// 球メッシュを作成
    /// </summary>
    public static BasicMesh CreateSphere(float radius = 1.0f, int segments = 32, int rings = 16)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var texCoords = new List<Vector2>();
        var indices = new List<uint>();

        for (int ring = 0; ring <= rings; ring++)
        {
            float theta = ring * MathF.PI / rings;
            float sinTheta = MathF.Sin(theta);
            float cosTheta = MathF.Cos(theta);

            for (int segment = 0; segment <= segments; segment++)
            {
                float phi = segment * 2.0f * MathF.PI / segments;
                float sinPhi = MathF.Sin(phi);
                float cosPhi = MathF.Cos(phi);

                Vector3 position = new Vector3(
                    radius * sinTheta * cosPhi,
                    radius * cosTheta,
                    radius * sinTheta * sinPhi
                );

                Vector3 normal = Vector3.Normalize(position);
                Vector2 texCoord = new Vector2(
                    (float)segment / segments,
                    (float)ring / rings
                );

                vertices.Add(position);
                normals.Add(normal);
                texCoords.Add(texCoord);
            }
        }

        // Generate indices
        for (int ring = 0; ring < rings; ring++)
        {
            for (int segment = 0; segment < segments; segment++)
            {
                uint current = (uint)(ring * (segments + 1) + segment);
                uint next = current + (uint)(segments + 1);

                // First triangle
                indices.Add(current);
                indices.Add(next);
                indices.Add(current + 1);

                // Second triangle
                indices.Add(current + 1);
                indices.Add(next);
                indices.Add(next + 1);
            }
        }

        return new BasicMesh(vertices.ToArray(), normals.ToArray(), texCoords.ToArray(), indices.ToArray());
    }

    /// <summary>
    /// 平面メッシュを作成
    /// </summary>
    public static BasicMesh CreatePlane(Vector2 size, int subdivisions = 1)
    {
        float halfWidth = size.X * 0.5f;
        float halfHeight = size.Y * 0.5f;
        
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var texCoords = new List<Vector2>();
        var indices = new List<uint>();

        // Generate vertices
        for (int y = 0; y <= subdivisions; y++)
        {
            for (int x = 0; x <= subdivisions; x++)
            {
                float xPos = (float)x / subdivisions * size.X - halfWidth;
                float zPos = (float)y / subdivisions * size.Y - halfHeight;

                vertices.Add(new Vector3(xPos, 0, zPos));
                normals.Add(Vector3.UnitY);
                texCoords.Add(new Vector2((float)x / subdivisions, (float)y / subdivisions));
            }
        }

        // Generate indices
        for (int y = 0; y < subdivisions; y++)
        {
            for (int x = 0; x < subdivisions; x++)
            {
                uint topLeft = (uint)(y * (subdivisions + 1) + x);
                uint topRight = topLeft + 1;
                uint bottomLeft = (uint)((y + 1) * (subdivisions + 1) + x);
                uint bottomRight = bottomLeft + 1;

                // First triangle
                indices.Add(topLeft);
                indices.Add(bottomLeft);
                indices.Add(topRight);

                // Second triangle
                indices.Add(topRight);
                indices.Add(bottomLeft);
                indices.Add(bottomRight);
            }
        }

        return new BasicMesh(vertices.ToArray(), normals.ToArray(), texCoords.ToArray(), indices.ToArray());
    }

    /// <summary>
    /// コーンメッシュを作成
    /// </summary>
    public static BasicMesh CreateCone(float radius = 1.0f, float height = 2.0f, int segments = 8)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var texCoords = new List<Vector2>();
        var indices = new List<uint>();

        // Top vertex
        vertices.Add(new Vector3(0, height * 0.5f, 0));
        normals.Add(Vector3.UnitY);
        texCoords.Add(new Vector2(0.5f, 0.5f));

        // Base center
        vertices.Add(new Vector3(0, -height * 0.5f, 0));
        normals.Add(-Vector3.UnitY);
        texCoords.Add(new Vector2(0.5f, 0.5f));

        // Base vertices
        for (int i = 0; i < segments; i++)
        {
            float angle = 2.0f * MathF.PI * i / segments;
            float x = radius * MathF.Cos(angle);
            float z = radius * MathF.Sin(angle);

            vertices.Add(new Vector3(x, -height * 0.5f, z));
            normals.Add(-Vector3.UnitY);
            texCoords.Add(new Vector2(
                0.5f + 0.5f * MathF.Cos(angle),
                0.5f + 0.5f * MathF.Sin(angle)
            ));
        }

        // Generate indices
        for (int i = 0; i < segments; i++)
        {
            int next = (i + 1) % segments;

            // Side triangle (from top to base)
            indices.Add(0); // top
            indices.Add((uint)(i + 2)); // current base vertex
            indices.Add((uint)(next + 2)); // next base vertex

            // Base triangle
            indices.Add(1); // base center
            indices.Add((uint)(next + 2)); // next base vertex
            indices.Add((uint)(i + 2)); // current base vertex
        }

        return new BasicMesh(vertices.ToArray(), normals.ToArray(), texCoords.ToArray(), indices.ToArray());
    }
}