using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics3D.Meshes;
using Beutl.Language;

namespace Beutl.Graphics3D.Models;

/// <summary>
/// A 3D object that holds a single mesh.
/// </summary>
[Display(Name = nameof(Strings.MeshObject3D), ResourceType = typeof(Strings))]
public sealed partial class MeshObject3D : Object3D
{
    public MeshObject3D()
    {
        ScanProperties<MeshObject3D>();
    }

    /// <summary>
    /// Gets or sets the mesh for this object.
    /// </summary>
    [Display(Name = nameof(Strings.Mesh), ResourceType = typeof(Strings))]
    public IProperty<Mesh?> Mesh { get; } = Property.Create<Mesh?>(null);

    public partial class Resource
    {
        public override Mesh.Resource? GetMesh() => Mesh;
    }
}
