using System.Numerics;
using System.Text.Json;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Models;
using Beutl.Media;
using Beutl.Serialization;

namespace Beutl.UnitTests.Engine.Graphics3D;

public sealed class ModelImportTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp() => _root = Directory.CreateTempSubdirectory("model-import-").FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    [Test]
    public void Model3D_LoadsBothMeshesSharingOneMaterial()
    {
        string path = Path.Combine(_root, "shared.obj");
        File.WriteAllText(path, """
            o Left
            v 0 0 0
            v 1 0 0
            v 0 1 0
            v 2 0 0
            v 3 0 0
            v 2 1 0
            f 1 2 3
            o Right
            f 4 5 6
            """);
        var source = new ModelSource();
        source.ReadFrom(new Uri(path));
        Assert.That(source.MeshCount, Is.EqualTo(2));
        Assert.That(source.MaterialCount, Is.EqualTo(1));

        var model = new Model3D();
        model.Source.CurrentValue = source;

        Assert.That(model.Children, Has.Count.EqualTo(2));
        Assert.That(model.Children.All(child => child.Material.CurrentValue is PBRMaterial), Is.True);
    }

    [Test]
    public void Model3D_UsesEachMeshMaterialInsteadOfItsPositionInTheMeshList()
    {
        File.WriteAllText(Path.Combine(_root, "colors.mtl"), """
            newmtl Red
            Kd 1 0 0
            newmtl Green
            Kd 0 1 0
            """);
        string path = Path.Combine(_root, "colors.obj");
        File.WriteAllText(path, """
            mtllib colors.mtl
            o GreenTriangle
            v 0 0 0
            v 1 0 0
            v 0 1 0
            v 2 0 0
            v 3 0 0
            v 2 1 0
            usemtl Green
            f 1 2 3
            o RedTriangle
            usemtl Red
            f 4 5 6
            """);
        var source = new ModelSource();
        source.ReadFrom(new Uri(path));

        var model = new Model3D();
        model.Source.CurrentValue = source;

        Assert.That(model.Children, Has.Count.EqualTo(2));
        Assert.That(((PBRMaterial)model.Children[0].Material.CurrentValue!).Albedo.CurrentValue,
            Is.EqualTo(Color.FromRgb(0, 255, 0)));
        Assert.That(((PBRMaterial)model.Children[1].Material.CurrentValue!).Albedo.CurrentValue,
            Is.EqualTo(Color.FromRgb(255, 0, 0)));
    }

    [Test]
    public void ModelSource_AppliesParentTransformsAndKeepsTransformedInstancesSeparate()
    {
        // Two nodes instance the same triangle. The first inherits a translation and a
        // quarter-turn from its parent, then scales each local axis by a different amount.
        string path = WriteGltf(
        [
            new { translation = new[] { 10f, 20f, 30f }, rotation = new[] { 0f, 0f, MathF.Sqrt(0.5f), MathF.Sqrt(0.5f) }, children = new[] { 1 } },
            new { mesh = 0, scale = new[] { 2f, 3f, 4f } },
            new { mesh = 0, translation = new[] { -5f, 0f, 0f } }
        ], [0, 2]);
        var source = new ModelSource();

        source.ReadFrom(new Uri(path));

        Assert.That(source.MeshCount, Is.EqualTo(2));
        AssertVertices(source.GetMeshData(0), [new(10, 20, 30), new(10, 22, 30), new(7, 20, 34)]);
        AssertVertices(source.GetMeshData(1), [new(-5, 0, 0), new(-4, 0, 0), new(-5, 1, 1)]);
        foreach (var vertex in source.GetMeshData(0).Vertices)
            AssertVector(vertex.Normal, new Vector3(0.8f, 0, 0.6f));
        foreach (var vertex in source.GetMeshData(1).Vertices)
            AssertVector(vertex.Normal, Vector3.Normalize(new Vector3(0, -1, 1)));
    }

    [Test]
    public void ModelSource_MirroredNodesKeepWindingConsistentWithNormals()
    {
        var source = new ModelSource();
        source.ReadFrom(new Uri(WriteGltf([new { mesh = 0, scale = new[] { -1f, 1f, 1f } }], [0])));

        MeshData mesh = source.GetMeshData(0);

        AssertVertices(mesh, [new(0, 0, 0), new(-1, 0, 0), new(0, 1, 1)]);
        Vector3 a = mesh.Vertices[(int)mesh.Indices[0]].Position;
        Vector3 b = mesh.Vertices[(int)mesh.Indices[1]].Position;
        Vector3 c = mesh.Vertices[(int)mesh.Indices[2]].Position;
        // Beutl asks Assimp for clockwise winding, opposite the supplied normals.
        Assert.That(Vector3.Dot(Vector3.Normalize(Vector3.Cross(b - a, c - a)), mesh.Vertices[0].Normal),
            Is.EqualTo(-1f).Within(0.0001f));
    }

    [Test]
    public void Model3D_SaveAndReopenKeepsBakedPositionsWithoutApplyingTheTransformAgain()
    {
        var source = new ModelSource();
        source.ReadFrom(new Uri(WriteGltf([new { mesh = 0, translation = new[] { 5f, 0f, 0f } }], [0])));
        var model = new Model3D();
        model.Source.CurrentValue = source;
        var uri = new Uri(Path.Combine(_root, "model.json"));
        CoreSerializer.StoreToUri(model, uri);

        Model3D restored = CoreSerializer.RestoreFromUri<Model3D>(uri);

        var child = (MeshObject3D)restored.Children.Single();
        var mesh = (ModelMesh)child.Mesh.CurrentValue!;
        Assert.That(mesh.Vertices.CurrentValue.Select(vertex => vertex.Position.X),
            Is.EquivalentTo(new[] { 5f, 6f, 5f }));
        Assert.That(child.Position.CurrentValue, Is.EqualTo(Vector3.Zero));
        Assert.That(child.Scale.CurrentValue, Is.EqualTo(Vector3.One));
    }

    private string WriteGltf(object[] nodes, int[] roots)
    {
        Vector3 normal = Vector3.Normalize(new Vector3(0, -1, 1));
        float[] data =
        [
            0, 0, 0, 1, 0, 0, 0, 1, 1,
            normal.X, normal.Y, normal.Z, normal.X, normal.Y, normal.Z, normal.X, normal.Y, normal.Z,
        ];
        byte[] buffer = new byte[data.Length * sizeof(float)];
        Buffer.BlockCopy(data, 0, buffer, 0, buffer.Length);
        var gltf = new
        {
            asset = new { version = "2.0" },
            scene = 0,
            scenes = new[] { new { nodes = roots } },
            nodes,
            meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0, NORMAL = 1 }, mode = 4 } } } },
            buffers = new[] { new { uri = "data:application/octet-stream;base64," + Convert.ToBase64String(buffer), byteLength = buffer.Length } },
            bufferViews = new[]
            {
                new { buffer = 0, byteOffset = 0, byteLength = 36, target = 34962 },
                new { buffer = 0, byteOffset = 36, byteLength = 36, target = 34962 },
            },
            accessors = new object[]
            {
                new { bufferView = 0, componentType = 5126, count = 3, type = "VEC3", min = new[] { 0f, 0f, 0f }, max = new[] { 1f, 1f, 1f } },
                new { bufferView = 1, componentType = 5126, count = 3, type = "VEC3" },
            }
        };
        string path = Path.Combine(_root, "model.gltf");
        File.WriteAllText(path, JsonSerializer.Serialize(gltf));
        return path;
    }

    private static void AssertVertices(MeshData mesh, Vector3[] expected)
    {
        Assert.That(mesh.Vertices, Has.Length.EqualTo(expected.Length));
        foreach (Vector3 point in expected)
            Assert.That(mesh.Vertices.Any(vertex => Vector3.Distance(vertex.Position, point) < 0.0001f), Is.True,
                $"Expected transformed vertex {point}.");
    }

    private static void AssertVector(Vector3 actual, Vector3 expected)
        => Assert.That(Vector3.Distance(actual, expected), Is.LessThan(0.0001f));
}
