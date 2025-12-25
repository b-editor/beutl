using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Graphics.Backend.Vulkan3D;
using Beutl.Graphics3D.Meshes;

namespace Beutl.Graphics3D.Primitives;

/// <summary>
/// A 3D cube primitive.
/// </summary>
public partial class Cube3D : Object3D
{
    public Cube3D()
    {
        ScanProperties<Cube3D>();
    }

    /// <summary>
    /// Gets the width of the cube (X-axis).
    /// </summary>
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Width { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the height of the cube (Y-axis).
    /// </summary>
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Height { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the depth of the cube (Z-axis).
    /// </summary>
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Depth { get; } = Property.CreateAnimatable(1f);

    /// <inheritdoc />
    /// <inheritdoc />
    public override Mesh GetMesh(Object3D.Resource resource)
    {
        var cubeResource = (Resource)resource;
        return CubeMesh.GenerateMesh(cubeResource.Width, cubeResource.Height, cubeResource.Depth);
    }


}
