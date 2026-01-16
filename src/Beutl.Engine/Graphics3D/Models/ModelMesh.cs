using System.Collections.Immutable;
using Beutl.Engine;
using Beutl.Graphics3D.Meshes;

namespace Beutl.Graphics3D.Models;

/// <summary>
/// A mesh loaded from a 3D model file.
/// </summary>
public sealed partial class ModelMesh : Mesh
{
    public ModelMesh()
    {
        ScanProperties<ModelMesh>();
    }

    public IProperty<ImmutableArray<Vertex3D>> Vertices { get; } = Property.Create<ImmutableArray<Vertex3D>>([]);

    public IProperty<ImmutableArray<uint>> Indices { get; } = Property.Create<ImmutableArray<uint>>([]);

    /// <inheritdoc />
    public override void ApplyTo(Mesh.Resource resource, out Vertex3D[] vertices, out uint[] indices)
    {
        var r = (Resource)resource;
        vertices = [..r.Vertices];
        indices = [..r.Indices];
    }
}
