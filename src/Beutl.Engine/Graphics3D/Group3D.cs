using Beutl.Engine;
using Beutl.Graphics3D.Meshes;

namespace Beutl.Graphics3D;

/// <summary>
/// A container that holds multiple 3D objects as children.
/// The group's transform is applied to all children.
/// </summary>
public partial class Group3D : Object3D
{
    public Group3D()
    {
        ScanProperties<Group3D>();
        Material.CurrentValue = null;
    }

    /// <summary>
    /// Gets the child objects in this group.
    /// </summary>
    public IListProperty<Object3D> Children { get; } = Property.CreateList<Object3D>();

    public partial class Resource
    {
        /// <inheritdoc />
        public override IReadOnlyList<Object3D.Resource> GetChildResources() => Children;

        /// <inheritdoc />
        public override Mesh.Resource? GetMesh() => null;
    }
}
