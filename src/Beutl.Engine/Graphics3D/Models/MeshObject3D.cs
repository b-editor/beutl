using Beutl.Engine;
using Beutl.Graphics3D.Meshes;

namespace Beutl.Graphics3D.Models;

/// <summary>
/// A 3D object that holds a single mesh.
/// </summary>
public sealed partial class MeshObject3D : Object3D
{
    public MeshObject3D()
    {
        ScanProperties<MeshObject3D>();
    }

    /// <summary>
    /// Gets or sets the mesh for this object.
    /// </summary>
    public IProperty<Mesh?> Mesh { get; } = Property.Create<Mesh?>(null);

    public partial class Resource
    {
        public override Mesh.Resource? GetMesh() => Mesh;
    }
}
