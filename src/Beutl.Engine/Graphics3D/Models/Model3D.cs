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
        Scale.Value = new System.Numerics.Vector3(100);
        Source.ValueChanged += (_, _) => SourceChanged();
    }

    /// <summary>
    /// Gets the model source to load.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Source), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<ModelSource?> Source { get; } = Property.CreateExpression<ModelSource?>(null);

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
        if (_deserializing || HistoryReplay.IsActive)
            return;

        Children.Clear();
        if (Source.Value is { HasUri: true })
            SynchronizeSourceGeometry();
    }

    internal void SynchronizeSourceGeometry()
    {
        if (Source.Value is not { } source) return;
        MeshObject3D[] previous = Children.OfType<MeshObject3D>().ToArray();
        static int Origin(MeshObject3D child) => child.Mesh.Value is ModelMesh mesh && mesh.SourceChildId == child.Id.Value
            ? mesh.SourceMeshIndex : -1;
        bool hasOrigins = previous.Any(child => Origin(child) >= 0);
        // Legacy scenes have no provenance. Position matching is best-effort;
        // keep unmarked surplus children rather than deleting possible user edits.
        for (int i = 0; i < source.MeshCount; i++)
        {
            var data = source.GetMeshData(i);
            MeshObject3D? child = previous.FirstOrDefault(child => Origin(child) == i);
            if (child == null && !hasOrigins && i < previous.Length) child = previous[i];
            bool created = child == null;
            child ??= new MeshObject3D();
            if (child.Mesh.Value is not ModelMesh mesh)
            {
                mesh = new ModelMesh();
                child.Mesh.Value = mesh;
            }
            mesh.Vertices.Value = data.Vertices;
            mesh.Indices.Value = data.Indices;
            mesh.SourceChildId = child.Id.Value;
            mesh.SourceMeshIndex = i;
            // A new child is filled in before it is added, so history records only the addition.
            if (created)
            {
                if (data.MaterialIndex >= 0 && data.MaterialIndex < source.MaterialCount)
                    child.Material.Value = CreateMaterial(source.GetMaterialData(data.MaterialIndex));
                Children.Add(child);
            }
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
            (Child: child, Mesh: child.Mesh.Value,
                Vertices: (child.Mesh.Value as ModelMesh)?.Vertices.Value,
                Indices: (child.Mesh.Value as ModelMesh)?.Indices.Value,
                Origin: (child.Mesh.Value as ModelMesh)?.SourceChildId,
                Index: (child.Mesh.Value as ModelMesh)?.SourceMeshIndex)).ToArray();
        return () =>
        {
            Children.Clear();
            foreach (var child in children) Children.Add(child);
            foreach (var state in meshes)
            {
                state.Child.Mesh.Value = state.Mesh;
                if (state.Mesh is ModelMesh mesh)
                {
                    mesh.Vertices.Value = state.Vertices!.Value;
                    mesh.Indices.Value = state.Indices!.Value;
                    mesh.SourceChildId = state.Origin!.Value;
                    mesh.SourceMeshIndex = state.Index!.Value;
                }
            }
        };
    }

    private static PBRMaterial CreateMaterial(MaterialData materialData)
    {
        var material = new PBRMaterial();
        material.Albedo.Value = materialData.Albedo;
        material.Emissive.Value = materialData.Emissive;
        material.Metallic.Value = materialData.Metallic;
        material.Roughness.Value = materialData.Roughness;

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
                map.Value = textureSource;
        }
    }

    private static ImageTextureSource? CreateTextureSource(string path)
    {
        try
        {
            var imageSource = new ImageSource();
            imageSource.ReadFrom(new Uri(path));

            var textureSource = new ImageTextureSource();
            textureSource.Source.Value = imageSource;

            return textureSource;
        }
        catch
        {
            return null;
        }
    }
}
