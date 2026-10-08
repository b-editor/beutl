using System.ComponentModel.DataAnnotations;
using Beutl.Editor;
using Beutl.Engine;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Textures;
using Beutl.Language;
using Beutl.Media.Source;
using Beutl.Serialization;

namespace Beutl.Graphics3D.Models;

/// <summary>
/// A 3D object that renders a model loaded from a file.
/// </summary>
[Display(Name = nameof(GraphicsStrings.Model3D), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Model3D : Group3D
{
    private bool _deserializing;

    public Model3D()
    {
        ScanProperties<Model3D>();
        // Model files are usually authored in meters; show one meter as 100 px.
        Scale.CurrentValue = new System.Numerics.Vector3(100);
        Source.ValueChanged += (_, _) => SourceChanged();
    }

    /// <summary>
    /// Gets the model source to load.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Source), ResourceType = typeof(GraphicsStrings))]
    public IProperty<ModelSource?> Source { get; } = Property.Create<ModelSource?>(null);

    public override void Deserialize(ICoreSerializationContext context)
    {
        _deserializing = true;
        try
        {
            base.Deserialize(context);
        }
        finally
        {
            _deserializing = false;
        }
    }

    internal void SourceChanged()
    {
        // History restores the recorded children along with Source. Regenerating
        // them here would replay the collection edits twice and lose their IDs.
        if (_deserializing || RecordingSuppression.IsSuppressed)
            return;

        var source = Source.CurrentValue;

        // Clear existing children
        Children.Clear();

        if (source is not { HasUri: true })
            return;

        // Create MeshObject3D for each mesh in the model
        for (int i = 0; i < source.MeshCount; i++)
        {
            var meshData = source.GetMeshData(i);

            // Create ModelMesh with vertices and indices
            var modelMesh = new ModelMesh();
            modelMesh.Vertices.CurrentValue = meshData.Vertices;
            modelMesh.Indices.CurrentValue = meshData.Indices;

            // Create MeshObject3D wrapper
            var meshObject = new MeshObject3D();
            meshObject.Mesh.CurrentValue = modelMesh;
            modelMesh.SourceChildId = meshObject.Id;
            modelMesh.SourceMeshIndex = i;

            // Set material if available
            if (meshData.MaterialIndex >= 0 && meshData.MaterialIndex < source.MaterialCount)
            {
                var materialData = source.GetMaterialData(meshData.MaterialIndex);
                var material = CreateMaterial(materialData);
                meshObject.Material.CurrentValue = material;
            }

            Children.Add(meshObject);
        }
    }

    internal void SynchronizeSourceGeometry()
    {
        if (Source.CurrentValue is not { } source) return;
        MeshObject3D[] previous = Children.OfType<MeshObject3D>().ToArray();
        static int Origin(MeshObject3D child) => child.Mesh.CurrentValue is ModelMesh mesh && mesh.SourceChildId == child.Id
            ? mesh.SourceMeshIndex : -1;
        bool hasOrigins = previous.Any(child => Origin(child) >= 0);
        // Legacy scenes have no provenance. Position matching is best-effort;
        // keep unmarked surplus children rather than deleting possible user edits.
        for (int i = 0; i < source.MeshCount; i++)
        {
            var data = source.GetMeshData(i);
            MeshObject3D child;
            MeshObject3D? imported = previous.FirstOrDefault(child => Origin(child) == i);
            if (imported != null) child = imported;
            else if (!hasOrigins && i < previous.Length) child = previous[i];
            else
            {
                child = new MeshObject3D();
                if (data.MaterialIndex >= 0 && data.MaterialIndex < source.MaterialCount)
                    child.Material.CurrentValue = CreateMaterial(source.GetMaterialData(data.MaterialIndex));
                Children.Add(child);
            }
            if (child.Mesh.CurrentValue is not ModelMesh mesh)
            {
                mesh = new ModelMesh();
                child.Mesh.CurrentValue = mesh;
            }
            mesh.Vertices.CurrentValue = data.Vertices;
            mesh.Indices.CurrentValue = data.Indices;
            mesh.SourceChildId = child.Id;
            mesh.SourceMeshIndex = i;
        }
        foreach (var child in previous.Where(child => Origin(child) >= source.MeshCount))
        {
            Children.Remove(child);
        }
    }

    internal Action CaptureGeometryRollback()
    {
        var children = Children.ToArray();
        var meshes = children.OfType<MeshObject3D>().Select(child =>
            (Child: child, Mesh: child.Mesh.CurrentValue,
                Vertices: (child.Mesh.CurrentValue as ModelMesh)?.Vertices.CurrentValue,
                Indices: (child.Mesh.CurrentValue as ModelMesh)?.Indices.CurrentValue,
                Origin: (child.Mesh.CurrentValue as ModelMesh)?.SourceChildId,
                Index: (child.Mesh.CurrentValue as ModelMesh)?.SourceMeshIndex)).ToArray();
        return () =>
        {
            Children.Clear();
            foreach (var child in children) Children.Add(child);
            foreach (var state in meshes)
            {
                state.Child.Mesh.CurrentValue = state.Mesh;
                if (state.Mesh is ModelMesh mesh)
                {
                    mesh.Vertices.CurrentValue = state.Vertices!.Value;
                    mesh.Indices.CurrentValue = state.Indices!.Value;
                    mesh.SourceChildId = state.Origin!.Value;
                    mesh.SourceMeshIndex = state.Index!.Value;
                }
            }
        };
    }

    private static PBRMaterial CreateMaterial(MaterialData materialData)
    {
        var material = new PBRMaterial();

        // Set albedo color
        material.Albedo.CurrentValue = materialData.Albedo;

        // Set emissive color
        material.Emissive.CurrentValue = materialData.Emissive;

        // Set metallic and roughness
        material.Metallic.CurrentValue = materialData.Metallic;
        material.Roughness.CurrentValue = materialData.Roughness;

        // Set texture maps
        TrySetTextureMap(material.AlbedoMap, materialData.AlbedoMapPath);
        TrySetTextureMap(material.NormalMap, materialData.NormalMapPath);
        TrySetTextureMap(material.MetallicRoughnessMap, materialData.MetallicRoughnessMapPath);
        TrySetTextureMap(material.EmissiveMap, materialData.EmissiveMapPath);
        TrySetTextureMap(material.AOMap, materialData.AOMapPath);

        return material;
    }

    /// <summary>
    /// Sets <paramref name="map"/> to the image at <paramref name="path"/>, leaving it unset when there is no path or
    /// the image cannot be loaded.
    /// </summary>
    private static void TrySetTextureMap(IProperty<TextureSource?> map, string? path)
    {
        if (path != null)
        {
            var textureSource = CreateTextureSource(path);
            if (textureSource != null)
                map.CurrentValue = textureSource;
        }
    }

    private static ImageTextureSource? CreateTextureSource(string path)
    {
        try
        {
            var imageSource = new ImageSource();
            imageSource.ReadFrom(new Uri(path));

            var textureSource = new ImageTextureSource();
            textureSource.Source.CurrentValue = imageSource;

            return textureSource;
        }
        catch
        {
            return null;
        }
    }
}
