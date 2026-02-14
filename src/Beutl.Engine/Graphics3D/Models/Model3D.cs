using System.ComponentModel.DataAnnotations;
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
[Display(Name = nameof(Strings.Model3D), ResourceType = typeof(Strings))]
public sealed partial class Model3D : Group3D
{
    private bool _deserializing;

    public Model3D()
    {
        ScanProperties<Model3D>();
        Source.ValueChanged += (_, _) => SourceChanged();
    }

    /// <summary>
    /// Gets the model source to load.
    /// </summary>
    [Display(Name = nameof(Strings.Source), ResourceType = typeof(Strings))]
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
        if (_deserializing)
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

            // Set material if available
            if (meshData.MaterialIndex >= 0 && meshData.MaterialIndex < source.MaterialCount)
            {
                var materialData = source.GetMaterialData(i);
                var material = CreateMaterial(materialData);
                meshObject.Material.CurrentValue = material;
            }

            Children.Add(meshObject);
        }
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

        // Set albedo map
        if (materialData.AlbedoMapPath != null)
        {
            var textureSource = CreateTextureSource(materialData.AlbedoMapPath);
            if (textureSource != null)
                material.AlbedoMap.CurrentValue = textureSource;
        }

        // Set normal map
        if (materialData.NormalMapPath != null)
        {
            var textureSource = CreateTextureSource(materialData.NormalMapPath);
            if (textureSource != null)
                material.NormalMap.CurrentValue = textureSource;
        }

        // Set metallic/roughness map
        if (materialData.MetallicRoughnessMapPath != null)
        {
            var textureSource = CreateTextureSource(materialData.MetallicRoughnessMapPath);
            if (textureSource != null)
                material.MetallicRoughnessMap.CurrentValue = textureSource;
        }

        // Set emissive map
        if (materialData.EmissiveMapPath != null)
        {
            var textureSource = CreateTextureSource(materialData.EmissiveMapPath);
            if (textureSource != null)
                material.EmissiveMap.CurrentValue = textureSource;
        }

        // Set AO map
        if (materialData.AOMapPath != null)
        {
            var textureSource = CreateTextureSource(materialData.AOMapPath);
            if (textureSource != null)
                material.AOMap.CurrentValue = textureSource;
        }

        return material;
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
