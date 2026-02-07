using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D.Meshes;
using Beutl.Language;

namespace Beutl.Graphics3D.Primitives;

/// <summary>
/// A 3D cube primitive.
/// </summary>
[Display(Name = nameof(Strings.Cube3D), ResourceType = typeof(Strings))]
public sealed partial class Cube3D : Object3D
{
    public Cube3D()
    {
        ScanProperties<Cube3D>();
    }

    /// <summary>
    /// Gets the width of the cube (X-axis).
    /// </summary>
    [Display(Name = nameof(Strings.Width), ResourceType = typeof(Strings))]
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Width { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the height of the cube (Y-axis).
    /// </summary>
    [Display(Name = nameof(Strings.Height), ResourceType = typeof(Strings))]
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Height { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the depth of the cube (Z-axis).
    /// </summary>
    [Display(Name = nameof(Strings.Depth), ResourceType = typeof(Strings))]
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Depth { get; } = Property.CreateAnimatable(1f);

    public partial class Resource
    {
        private readonly CubeMesh _mesh = new();
        private CubeMesh.Resource? _meshResource;

        partial void PostUpdate(Cube3D obj, RenderContext context)
        {
            _mesh.Width.CurrentValue = Math.Max(Width, 0.001f);
            _mesh.Height.CurrentValue = Math.Max(Height, 0.001f);
            _mesh.Depth.CurrentValue = Math.Max(Depth, 0.001f);

            if (_meshResource is null)
            {
                _meshResource = _mesh.ToResource(context);
                Version++;
            }
            else
            {
                if (_meshResource.GetOriginal() != _mesh)
                {
                    var oldMesh = _meshResource;
                    _meshResource = _mesh.ToResource(context);
                    oldMesh.Dispose();
                    Version++;
                }
                else
                {
                    var oldVersion = _meshResource.Version;
                    var _ = false;
                    _meshResource.Update(_mesh, context, ref _);
                    if (oldVersion != _meshResource.Version)
                    {
                        Version++;
                    }
                }
            }
        }

        partial void PostDispose(bool disposing)
        {
            _meshResource?.Dispose();
        }

        public override Mesh.Resource? GetMesh() => _meshResource;
    }
}
