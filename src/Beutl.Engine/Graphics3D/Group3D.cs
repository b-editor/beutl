using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics3D.Meshes;
using Beutl.Language;

namespace Beutl.Graphics3D;

/// <summary>
/// A container that holds multiple 3D objects as children.
/// The group's transform is applied to all children.
/// </summary>
[Display(Name = nameof(GraphicsStrings.Group3D), ResourceType = typeof(GraphicsStrings))]
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
    [Display(Name = nameof(GraphicsStrings.Group3D_Children), ResourceType = typeof(GraphicsStrings))]
    public IListProperty<Object3D> Children { get; } = Property.CreateList<Object3D>();

    public partial class Resource
    {
        /// <inheritdoc />
        public override IReadOnlyList<Object3D.Resource> GetChildResources() => Children;

        /// <inheritdoc />
        public override Mesh.Resource? GetMesh() => null;
    }
}
