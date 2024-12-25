using System.Numerics;

namespace Beutl.Graphics3D.Meshes;

public class BallMesh : Mesh
{
    public BallMesh()
    {
        GenerateBall(1.0f, 32, 32);
    }

    private void GenerateBall(float radius, int slices, int stacks)
    {
        float phi, theta;
        float x, y, z;
        float s, t;

        float dphi = MathF.PI / stacks;
        float dtheta = 2.0f * MathF.PI / slices;

        for (int i = 0; i <= stacks; i++)
        {
            phi = MathF.PI / 2.0f - i * dphi;
            t = 1.0f - (float)i / stacks;

            for (int j = 0; j <= slices; j++)
            {
                theta = j * dtheta;
                s = (float)j / slices;

                x = MathF.Sin(phi) * MathF.Cos(theta);
                y = MathF.Cos(phi);
                z = MathF.Sin(phi) * MathF.Sin(theta);

                var position = new Vector3(x, y, z) * radius;
                var color = new Vector3(1.0f, 1.0f, 1.0f);
                var normal = new Vector3(x, y, z);
                var texCoord = new Vector2(s, t);

                var v = new Vertex(position, color, normal, texCoord);
                Vertices.Add(v);
            }
        }

        Indices = new int[6 * slices * stacks];
        int index = 0;

        for (int i = 0; i < stacks; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                Indices[index++] = (i + 1) * (slices + 1) + j;
                Indices[index++] = i * (slices + 1) + j;
                Indices[index++] = i * (slices + 1) + j + 1;

                Indices[index++] = (i + 1) * (slices + 1) + j;
                Indices[index++] = i * (slices + 1) + j + 1;
                Indices[index++] = (i + 1) * (slices + 1) + j + 1;
            }
        }

        Bounds = GenerateAABB();
    }
}
