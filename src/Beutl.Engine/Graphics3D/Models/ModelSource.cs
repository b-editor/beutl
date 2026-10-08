using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json.Serialization;
using Beutl.Engine;
using Beutl.Graphics3D.Meshes;
using Beutl.IO;
using Beutl.Media;
using Beutl.Serialization;
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
    private Matrix4x4 _toYUp = Matrix4x4.Identity;
    private readonly HashSet<string> _dependencies = new(StringComparer.Ordinal);
    private readonly Dictionary<nint, string> _embeddedTextures = [];

    internal IReadOnlyCollection<string> Dependencies => _dependencies;

    public new Uri Uri
    {
        get => _uri ?? throw new InvalidOperationException("URI is not set.");
        protected set
        {
            if (_uri == value) return;
            _uri = value;
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Uri)));
        }
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
        _dependencies.Clear();
        _embeddedTextures.Clear();

        string path = uri.LocalPath;
        if (!System.IO.File.Exists(path))
            throw new FileNotFoundException($"Model file not found: {path}");

        _basePath = System.IO.Path.GetDirectoryName(path);
        LoadWithAssimp(path);
        RaiseEdited();
    }

    internal void RelinkFrom(ModelSource loaded)
    {
        Uri = loaded.Uri;
        _basePath = loaded._basePath;
        _toYUp = loaded._toYUp;
        _meshDataList.Clear();
        _meshDataList.AddRange(loaded._meshDataList);
        _materialDataList.Clear();
        _materialDataList.AddRange(loaded._materialDataList);
        _dependencies.Clear();
        _dependencies.UnionWith(loaded._dependencies);
        _embeddedTextures.Clear();
        foreach (var pair in loaded._embeddedTextures) _embeddedTextures.Add(pair.Key, pair.Value);
        RaiseEdited();
    }

    internal ModelSource CaptureState()
    {
        var snapshot = new ModelSource();
        snapshot.RelinkFrom(this);
        return snapshot;
    }

    private unsafe void LoadWithAssimp(string path)
    {
        using var assimp = Assimp.GetApi();
        using var fileSystem = new ModelImportFileIO(_basePath!);
        FileIO fileIO = fileSystem.FileIO;
        var properties = assimp.CreatePropertyStore();
        if (properties == null)
            throw new OutOfMemoryException("Could not allocate Assimp import properties.");

        Scene* scene = null;
        try
        {
            // Beutl stores static mesh vertices, not Assimp's node transforms. Bake the
            // complete hierarchy into them, keeping separate meshes and transformed instances.
            assimp.SetImportPropertyInteger(properties, "PP_PTV_KEEP_HIERARCHY", 1);
            scene = assimp.ImportFileExWithProperties(
                path,
                (uint)(PostProcessSteps.Triangulate |
                       PostProcessSteps.GenerateNormals |
                       PostProcessSteps.CalculateTangentSpace |
                       PostProcessSteps.JoinIdenticalVertices |
                       PostProcessSteps.PreTransformVertices |
                       PostProcessSteps.FlipWindingOrder |
                       PostProcessSteps.FlipUVs),
                &fileIO,
                properties);

            if (scene == null ||
                (scene->MFlags & (uint)SceneFlags.Incomplete) != 0 ||
                scene->MRootNode == null)
            {
                var error = assimp.GetErrorStringS();
                if (fileSystem.MissingPaths.Count > 0)
                    throw new InvalidOperationException($"Failed to load model: {error}",
                        new FileNotFoundException("A required model file is missing.", fileSystem.MissingPaths.First()));
                throw new InvalidOperationException($"Failed to load model: {error}");
            }

            // Process materials first
            _toYUp = GetRotationToYUp(scene, path);
            ProcessMaterials(assimp, scene);

            // Process nodes and meshes
            ProcessNode(scene->MRootNode, scene);
            _dependencies.UnionWith(fileSystem.Paths);
        }
        finally
        {
            if (scene != null)
                assimp.ReleaseImport(scene);
            assimp.ReleasePropertyStore(properties);
        }
    }

    // The rotation that turns the file's up axis into +Y. Formats that record it (FBX among them) say so in
    // the scene metadata; 3DS and Blender files are Z-up by convention; anything else is taken as Y-up.
    private static unsafe Matrix4x4 GetRotationToYUp(Scene* scene, string path)
    {
        int? upAxis = null;
        int upSign = 1;
        Metadata* metadata = scene->MMetaData;
        if (metadata != null)
        {
            for (uint i = 0; i < metadata->MNumProperties; i++)
            {
                MetadataEntry entry = metadata->MValues[i];
                if (entry.MType != MetadataType.Int32 || entry.MData == null)
                    continue;

                string key = metadata->MKeys[i].AsString;
                if (key == "UpAxis")
                    upAxis = *(int*)entry.MData;
                else if (key == "UpAxisSign")
                    upSign = *(int*)entry.MData < 0 ? -1 : 1;
            }
        }

        upAxis ??= System.IO.Path.GetExtension(path).ToLowerInvariant() is ".3ds" or ".blend" ? 2 : 1;
        float half = MathF.PI / 2;
        return (upAxis, upSign) switch
        {
            (0, 1) => Matrix4x4.CreateRotationZ(half),
            (0, _) => Matrix4x4.CreateRotationZ(-half),
            (2, 1) => Matrix4x4.CreateRotationX(-half),
            (2, _) => Matrix4x4.CreateRotationX(half),
            (_, -1) => Matrix4x4.CreateRotationX(MathF.PI),
            _ => Matrix4x4.Identity,
        };
    }

    private unsafe void ProcessMaterials(Assimp assimp, Scene* scene)
    {
        for (uint i = 0; i < scene->MNumMaterials; i++)
        {
            var material = scene->MMaterials[i];
            ProcessMaterial(assimp, scene, material);
        }
    }

    private unsafe void ProcessMaterial(Assimp assimp, Scene* scene, Material* material)
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
        string? albedoMapPath = GetTexturePath(assimp, scene, material, TextureType.Diffuse, TextureType.BaseColor);
        string? normalMapPath = GetTexturePath(assimp, scene, material, TextureType.Normals, TextureType.Height);
        string? metallicRoughnessMapPath = GetTexturePath(assimp, scene, material, TextureType.Unknown, TextureType.Metalness);
        string? emissiveMapPath = GetTexturePath(assimp, scene, material, TextureType.Emissive, TextureType.EmissionColor);
        string? aoMapPath = GetTexturePath(assimp, scene, material, TextureType.AmbientOcclusion, TextureType.Lightmap);

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

    /// <summary>
    /// Gets the texture path for <paramref name="type"/>, or for <paramref name="fallback"/> when the material has
    /// none of the first type.
    /// </summary>
    private unsafe string? GetTexturePath(
        Assimp assimp, Scene* scene, Material* material, TextureType type, TextureType fallback)
    {
        return GetTexturePath(assimp, scene, material, type) ?? GetTexturePath(assimp, scene, material, fallback);
    }

    private unsafe string? GetTexturePath(Assimp assimp, Scene* scene, Material* material, TextureType type)
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

        // Embedded textures use keys such as "*0", not filesystem paths. Keep the
        // encoded image in a data URI so it survives scene release, save and export.
        Texture* embedded = FindEmbeddedTexture(scene, pathStr.AsString);
        if (embedded != null)
        {
            if (!_embeddedTextures.TryGetValue((nint)embedded, out string? dataUri))
            {
                dataUri = EncodeEmbeddedTexture(embedded);
                _embeddedTextures.Add((nint)embedded, dataUri);
            }
            return dataUri;
        }

        string texturePath = pathStr.AsString.Replace('\\', Path.DirectorySeparatorChar);

        // If path is relative, resolve it relative to the model file
        if (!Path.IsPathRooted(texturePath) && _basePath != null)
        {
            texturePath = Path.Combine(_basePath, texturePath);
        }

        // Check if the texture file exists
        if (!System.IO.File.Exists(texturePath))
            return null;

        texturePath = Path.GetFullPath(texturePath);
        _dependencies.Add(texturePath);
        return texturePath;
    }

    internal static unsafe string EncodeEmbeddedTexture(Texture* texture)
    {
        if (texture->MHeight == 0)
        {
            byte[] encoded = new ReadOnlySpan<byte>(texture->PcData, checked((int)texture->MWidth)).ToArray();
            return UriHelper.CreateBase64DataUri("application/octet-stream", encoded).AbsoluteUri;
        }

        // aiTexel stores BGRA bytes; encode raw embedded images once for persistence.
        int width = checked((int)texture->MWidth);
        int height = checked((int)texture->MHeight);
        using var bitmap = new Bitmap(width, height);
        for (int y = 0; y < height; y++)
            new ReadOnlySpan<byte>(texture->PcData + checked(y * width), checked(width * 4)).CopyTo(bitmap.GetRow(y));
        using var stream = new MemoryStream();
        if (!bitmap.Save(stream, Graphics.EncodedImageFormat.Png))
            throw new InvalidOperationException("Failed to encode an embedded model texture.");
        return UriHelper.CreateBase64DataUri("image/png", stream.ToArray()).AbsoluteUri;
    }

    private static unsafe Texture* FindEmbeddedTexture(Scene* scene, string key)
    {
        // Mirror Assimp's scene lookup. Some bundled builds do not export its C helper.
        if (key.StartsWith('*') && uint.TryParse(key.AsSpan(1), out uint index))
            return index < scene->MNumTextures ? scene->MTextures[index] : null;

        string name = Path.GetFileName(key.Replace('\\', '/'));
        for (uint i = 0; i < scene->MNumTextures; i++)
        {
            Texture* texture = scene->MTextures[i];
            if (Path.GetFileName(texture->MFilename.AsString.Replace('\\', '/')) == name)
                return texture;
        }
        return null;
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
            vertices.Add(ReadVertex(mesh, i));
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

    /// <summary>Reads vertex <paramref name="index"/> of <paramref name="mesh"/> into Beutl's Y-down space.</summary>
    private unsafe Vertex3D ReadVertex(Silk.NET.Assimp.Mesh* mesh, uint index)
    {
        var position = new Vector3(
            mesh->MVertices[index].X,
            mesh->MVertices[index].Y,
            mesh->MVertices[index].Z);

        var normal = mesh->MNormals != null
            ? new Vector3(mesh->MNormals[index].X, mesh->MNormals[index].Y, mesh->MNormals[index].Z)
            : Vector3.UnitY;

        var texCoord = Vector2.Zero;
        if (mesh->MTextureCoords[0] != null)
        {
            texCoord = new Vector2(
                mesh->MTextureCoords[0][index].X,
                mesh->MTextureCoords[0][index].Y);
        }

        var tangent = new Vector4(1, 0, 0, 1);
        if (mesh->MTangents != null && mesh->MBitangents != null)
        {
            var t = new Vector3(
                mesh->MTangents[index].X,
                mesh->MTangents[index].Y,
                mesh->MTangents[index].Z);
            var b = new Vector3(
                mesh->MBitangents[index].X,
                mesh->MBitangents[index].Y,
                mesh->MBitangents[index].Z);

            // Calculate handedness
            float handedness = Vector3.Dot(Vector3.Cross(normal, t), b) < 0 ? -1f : 1f;
            tangent = new Vector4(t, handedness);
        }

        // Assimp keeps the file's own up axis; turn it to Y-up, then into Beutl's Y-down space.
        Vector3 tangentDirection = Vector3.TransformNormal(new Vector3(tangent.X, tangent.Y, tangent.Z), _toYUp);
        return CoordinateSystem3D.FromYUp(new Vertex3D(
            Vector3.Transform(position, _toYUp),
            Vector3.TransformNormal(normal, _toYUp),
            texCoord,
            new Vector4(tangentDirection, tangent.W)));
    }
}
