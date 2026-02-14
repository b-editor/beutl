using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics3D.Meshes;
using Beutl.Language;

namespace Beutl.Graphics3D.Models;

/// <summary>
/// A mesh loaded from a 3D model file.
/// </summary>
[Display(Name = nameof(Strings.ModelMesh), ResourceType = typeof(Strings))]
public sealed partial class ModelMesh : Mesh
{
    public ModelMesh()
    {
        ScanProperties<ModelMesh>();
    }

    [Display(Name = nameof(Strings.Vertices), ResourceType = typeof(Strings))]
    public IProperty<ImmutableArray<Vertex3D>> Vertices { get; } = Property.Create<ImmutableArray<Vertex3D>>([]);

    [Display(Name = nameof(Strings.Indices), ResourceType = typeof(Strings))]
    public IProperty<ImmutableArray<uint>> Indices { get; } = Property.Create<ImmutableArray<uint>>([]);

    /// <inheritdoc />
    public override void ApplyTo(Mesh.Resource resource, out Vertex3D[] vertices, out uint[] indices)
    {
        var r = (Resource)resource;
        vertices = [.. r.Vertices];
        indices = [.. r.Indices];
    }
}
