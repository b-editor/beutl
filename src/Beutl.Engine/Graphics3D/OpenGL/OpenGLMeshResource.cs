using OpenTK.Graphics.OpenGL;

namespace Beutl.Graphics.Rendering.OpenGL;

/// <summary>
/// OpenGL用メッシュリソース
/// </summary>
public class OpenGLMeshResource : I3DMeshResource
{
    private bool _disposed;

    public uint VertexBufferId { get; private set; }
    public uint IndexBufferId { get; private set; }
    public uint VAO { get; private set; }
    public int IndexCount { get; private set; }
    public I3DMesh SourceMesh { get; }

    public OpenGLMeshResource(I3DMesh mesh)
    {
        SourceMesh = mesh;
        CreateBuffers();
    }

    private void CreateBuffers()
    {
        // Vertex Array Object を作成
        VAO = GL.GenVertexArray();
        GL.BindVertexArray(VAO);

        // 頂点データを準備
        var vertices = SourceMesh.Vertices;
        var normals = SourceMesh.Normals;
        var texCoords = SourceMesh.TexCoords;
        var indices = SourceMesh.Indices;

        // インターリーブした頂点データを作成
        float[] interleavedData = new float[vertices.Length * 8]; // position(3) + normal(3) + texcoord(2)
        for (int i = 0; i < vertices.Length; i++)
        {
            int baseIndex = i * 8;

            // Position
            interleavedData[baseIndex + 0] = vertices[i].X;
            interleavedData[baseIndex + 1] = vertices[i].Y;
            interleavedData[baseIndex + 2] = vertices[i].Z;

            // Normal
            if (i < normals.Length)
            {
                interleavedData[baseIndex + 3] = normals[i].X;
                interleavedData[baseIndex + 4] = normals[i].Y;
                interleavedData[baseIndex + 5] = normals[i].Z;
            }

            // TexCoord
            if (i < texCoords.Length)
            {
                interleavedData[baseIndex + 6] = texCoords[i].X;
                interleavedData[baseIndex + 7] = texCoords[i].Y;
            }
        }

        // 頂点バッファを作成
        VertexBufferId = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ArrayBuffer, VertexBufferId);
        GL.BufferData(BufferTarget.ArrayBuffer, interleavedData.Length * sizeof(float), interleavedData, BufferUsageHint.StaticDraw);

        // インデックスバッファを作成
        IndexBufferId = GL.GenBuffer();
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, IndexBufferId);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices.ToArray(), BufferUsageHint.StaticDraw);
        IndexCount = indices.Length;

        // 頂点属性を設定
        int stride = 8 * sizeof(float);

        // Position (location = 0)
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);

        // Normal (location = 1)
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));

        // TexCoord (location = 2)
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));

        // VAOをアンバインド
        GL.BindVertexArray(0);
        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, 0);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        GL.DeleteVertexArray(VAO);
        GL.DeleteBuffer(VertexBufferId);
        GL.DeleteBuffer(IndexBufferId);

        VAO = 0;
        VertexBufferId = 0;
        IndexBufferId = 0;

        _disposed = true;
    }
}
