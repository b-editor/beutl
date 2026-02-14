using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Serialization;
using Beutl.Engine;
using Beutl.Graphics3D.Meshes;
using Beutl.IO;
using Beutl.Media;
using Silk.NET.Assimp;

namespace Beutl.Graphics3D.Models;

/// <summary>
/// Represents mesh data loaded from a 3D model file.
/// </summary>
internal readonly record struct MeshData(
    ImmutableArray<Vertex3D> Vertices,
    ImmutableArray<uint> Indices,
    int MaterialIndex,
    string? Name
);

/// <summary>
/// Represents material data loaded from a 3D model file.
/// </summary>
internal readonly record struct MaterialData(
    Color Albedo,
    Color Emissive,
    float Metallic,
    float Roughness,
    float Opacity,
    string? AlbedoMapPath,
    string? NormalMapPath,
    string? MetallicRoughnessMapPath,
    string? EmissiveMapPath,
    string? AOMapPath,
    string? Name
);

/// <summary>
/// A source that loads 3D model files using Assimp.
/// </summary>
[JsonConverter(typeof(ModelSourceJsonConverter))]
[SuppressResourceClassGeneration]
public class ModelSource : EngineObject, IFileSource
{
    private Uri? _uri;
    private string? _basePath;
    private readonly List<MeshData> _meshDataList = [];
    private readonly List<MaterialData> _materialDataList = [];

    public new Uri Uri
    {
        get => _uri ?? throw new InvalidOperationException("URI is not set.");
        protected set => _uri = value;
    }

    public bool HasUri => _uri != null;

    /// <summary>
    /// Gets the number of meshes loaded from the model file.
    /// </summary>
    public int MeshCount => _meshDataList.Count;

    /// <summary>
    /// Gets the number of materials loaded from the model file.
    /// </summary>
    public int MaterialCount => _materialDataList.Count;

    /// <summary>
    /// Gets the mesh data at the specified index.
    /// </summary>
    internal MeshData GetMeshData(int index) => _meshDataList[index];

    /// <summary>
    /// Gets the material data at the specified index.
    /// </summary>
    internal MaterialData GetMaterialData(int index) => _materialDataList[index];

    /// <summary>
    /// Reads a 3D model from the specified URI using Assimp.
    /// </summary>
    public void ReadFrom(Uri uri)
    {
        Uri = uri;
        _meshDataList.Clear();
        _materialDataList.Clear();

        string path = uri.LocalPath;
        if (!System.IO.File.Exists(path))
            throw new FileNotFoundException($"Model file not found: {path}");

        _basePath = System.IO.Path.GetDirectoryName(path);
        LoadWithAssimp(path);
    }

    private unsafe void LoadWithAssimp(string path)
    {
        using var assimp = Assimp.GetApi();

        var scene = assimp.ImportFile(
            path,
            (uint)(PostProcessSteps.Triangulate |
                   PostProcessSteps.GenerateNormals |
                   PostProcessSteps.CalculateTangentSpace |
                   PostProcessSteps.JoinIdenticalVertices |
                   PostProcessSteps.FlipWindingOrder |
                   PostProcessSteps.FlipUVs));

        if (scene == null ||
            (scene->MFlags & (uint)SceneFlags.Incomplete) != 0 ||
            scene->MRootNode == null)
        {
            var error = assimp.GetErrorStringS();
            throw new InvalidOperationException($"Failed to load model: {error}");
        }

        // Process materials first
        ProcessMaterials(assimp, scene);

        // Process nodes and meshes
        ProcessNode(scene->MRootNode, scene);

        assimp.FreeScene(scene);
    }

    private unsafe void ProcessMaterials(Assimp assimp, Scene* scene)
    {
        for (uint i = 0; i < scene->MNumMaterials; i++)
        {
            var material = scene->MMaterials[i];
            ProcessMaterial(assimp, material);
        }
    }

    private unsafe void ProcessMaterial(Assimp assimp, Material* material)
    {
        // Get material name
        AssimpString nameStr = default;
        assimp.GetMaterialString(material, Assimp.MatkeyName, 0, 0, ref nameStr);
        string? name = nameStr.Length > 0 ? nameStr.AsString : null;

        // Get diffuse/albedo color
        Vector4 diffuseColor = new(1, 1, 1, 1);
        assimp.GetMaterialColor(material, Assimp.MatkeyColorDiffuse, 0, 0, ref diffuseColor);

        // Get emissive color
        Vector4 emissiveColor = new(0, 0, 0, 1);
        assimp.GetMaterialColor(material, Assimp.MatkeyColorEmissive, 0, 0, ref emissiveColor);

        // Get shininess (convert to roughness)
        float shininess = 0f;
        uint max = 1;
        assimp.GetMaterialFloatArray(material, Assimp.MatkeyShininess, 0, 0, ref shininess, ref max);
        // Convert shininess to roughness: roughness = 1 - sqrt(shininess / 256)
        float roughness = shininess > 0 ? 1f - MathF.Sqrt(MathF.Min(shininess, 256f) / 256f) : 0.5f;

        // Get opacity
        float opacity = 1f;
        max = 1;
        assimp.GetMaterialFloatArray(material, Assimp.MatkeyOpacity, 0, 0, ref opacity, ref max);

        // Get metallic (try PBR key first, fall back to reflectivity)
        float metallic = 0f;
        max = 1;
        // Try to get metallic factor from PBR materials (gltf, etc.)
        if (assimp.GetMaterialFloatArray(material, Assimp.MatkeyMetallicFactor, 0, 0, ref metallic, ref max) != Return.Success)
        {
            // Fall back to reflectivity as a proxy for metallic
            assimp.GetMaterialFloatArray(material, Assimp.MatkeyReflectivity, 0, 0, ref metallic, ref max);
        }

        // Try to get roughness from PBR materials
        float pbrRoughness = roughness;
        max = 1;
        if (assimp.GetMaterialFloatArray(material, Assimp.MatkeyRoughnessFactor, 0, 0, ref pbrRoughness, ref max) == Return.Success)
        {
            roughness = pbrRoughness;
        }

        // Get texture paths
        string? albedoMapPath = GetTexturePath(assimp, material, TextureType.Diffuse);
        if (albedoMapPath == null)
            albedoMapPath = GetTexturePath(assimp, material, TextureType.BaseColor);

        string? normalMapPath = GetTexturePath(assimp, material, TextureType.Normals);
        if (normalMapPath == null)
            normalMapPath = GetTexturePath(assimp, material, TextureType.Height);

        string? metallicRoughnessMapPath = GetTexturePath(assimp, material, TextureType.Unknown);
        if (metallicRoughnessMapPath == null)
            metallicRoughnessMapPath = GetTexturePath(assimp, material, TextureType.Metalness);

        string? emissiveMapPath = GetTexturePath(assimp, material, TextureType.Emissive);
        if (emissiveMapPath == null)
            emissiveMapPath = GetTexturePath(assimp, material, TextureType.EmissionColor);

        string? aoMapPath = GetTexturePath(assimp, material, TextureType.AmbientOcclusion);
        if (aoMapPath == null)
            aoMapPath = GetTexturePath(assimp, material, TextureType.Lightmap);

        _materialDataList.Add(new MaterialData(
            Color.FromArgb(
                (byte)(opacity * 255),
                (byte)(diffuseColor.X * 255),
                (byte)(diffuseColor.Y * 255),
                (byte)(diffuseColor.Z * 255)),
            Color.FromArgb(
                255,
                (byte)(emissiveColor.X * 255),
                (byte)(emissiveColor.Y * 255),
                (byte)(emissiveColor.Z * 255)),
            metallic,
            roughness,
            opacity,
            albedoMapPath,
            normalMapPath,
            metallicRoughnessMapPath,
            emissiveMapPath,
            aoMapPath,
            name));
    }

    private unsafe string? GetTexturePath(Assimp assimp, Material* material, TextureType type)
    {
        uint textureCount = assimp.GetMaterialTextureCount(material, type);
        if (textureCount == 0)
            return null;

        AssimpString pathStr = default;
        var result = assimp.GetMaterialTexture(
            material, type, 0, ref pathStr,
            null, null, null, null, null, null);

        if (result != Return.Success || pathStr.Length == 0)
            return null;

        string texturePath = pathStr.AsString.Replace(@"\\", Path.DirectorySeparatorChar.ToString());

        // If path is relative, resolve it relative to the model file
        if (!Path.IsPathRooted(texturePath) && _basePath != null)
        {
            texturePath = Path.Combine(_basePath, texturePath);
        }

        // Check if the texture file exists
        if (!System.IO.File.Exists(texturePath))
            return null;

        return texturePath;
    }

    private unsafe void ProcessNode(Node* node, Scene* scene)
    {
        // Process meshes in this node
        for (uint i = 0; i < node->MNumMeshes; i++)
        {
            var meshIndex = node->MMeshes[i];
            var mesh = scene->MMeshes[meshIndex];
            ProcessMesh(mesh);
        }

        // Process child nodes recursively
        for (uint i = 0; i < node->MNumChildren; i++)
        {
            ProcessNode(node->MChildren[i], scene);
        }
    }

    private unsafe void ProcessMesh(Silk.NET.Assimp.Mesh* mesh)
    {
        var vertices = new List<Vertex3D>((int)mesh->MNumVertices);
        var indices = new List<uint>();

        // Extract vertex data
        for (uint i = 0; i < mesh->MNumVertices; i++)
        {
            var position = new Vector3(
                mesh->MVertices[i].X,
                mesh->MVertices[i].Y,
                mesh->MVertices[i].Z);

            var normal = mesh->MNormals != null
                ? new Vector3(mesh->MNormals[i].X, mesh->MNormals[i].Y, mesh->MNormals[i].Z)
                : Vector3.UnitY;

            var texCoord = Vector2.Zero;
            if (mesh->MTextureCoords[0] != null)
            {
                texCoord = new Vector2(
                    mesh->MTextureCoords[0][i].X,
                    mesh->MTextureCoords[0][i].Y);
            }

            var tangent = new Vector4(1, 0, 0, 1);
            if (mesh->MTangents != null && mesh->MBitangents != null)
            {
                var t = new Vector3(
                    mesh->MTangents[i].X,
                    mesh->MTangents[i].Y,
                    mesh->MTangents[i].Z);
                var b = new Vector3(
                    mesh->MBitangents[i].X,
                    mesh->MBitangents[i].Y,
                    mesh->MBitangents[i].Z);

                // Calculate handedness
                float handedness = Vector3.Dot(Vector3.Cross(normal, t), b) < 0 ? -1f : 1f;
                tangent = new Vector4(t, handedness);
            }

            vertices.Add(new Vertex3D(position, normal, texCoord, tangent));
        }

        // Extract index data from faces
        for (uint i = 0; i < mesh->MNumFaces; i++)
        {
            var face = mesh->MFaces[i];
            for (uint j = 0; j < face.MNumIndices; j++)
            {
                indices.Add(face.MIndices[j]);
            }
        }

        // Get mesh name
        string? meshName = mesh->MName.Length > 0
            ? mesh->MName.AsString
            : null;

        _meshDataList.Add(new MeshData(
            [.. vertices],
            [.. indices],
            (int)mesh->MMaterialIndex,
            meshName));
    }
}
