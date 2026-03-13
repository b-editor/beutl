using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics3D.Meshes;
using Beutl.Language;

namespace Beutl.Graphics3D.Primitives;

/// <summary>
/// A 3D plane primitive.
/// </summary>
[Display(Name = nameof(GraphicsStrings.Plane3D), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Plane3D : Object3D
{
    public Plane3D()
    {
        ScanProperties<Plane3D>();
    }

    /// <summary>
    /// Gets the width of the plane (X-axis).
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Width), ResourceType = typeof(GraphicsStrings))]
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Width { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the height of the plane (Z-axis).
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Height), ResourceType = typeof(GraphicsStrings))]
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Height { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the number of segments along the width (X-axis).
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Plane3D_WidthSegments), ResourceType = typeof(GraphicsStrings))]
    [Range(1, int.MaxValue)]
    public IProperty<int> WidthSegments { get; } = Property.CreateAnimatable(1);

    /// <summary>
    /// Gets the number of segments along the height (Z-axis).
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Plane3D_HeightSegments), ResourceType = typeof(GraphicsStrings))]
    [Range(1, int.MaxValue)]
    public IProperty<int> HeightSegments { get; } = Property.CreateAnimatable(1);

    public partial class Resource
    {
        private readonly PlaneMesh _mesh = new();
        private PlaneMesh.Resource? _meshResource;

        partial void PostUpdate(Plane3D obj, CompositionContext context)
        {
            _mesh.Width.CurrentValue = Math.Max(Width, 0.001f);
            _mesh.Height.CurrentValue = Math.Max(Height, 0.001f);
            _mesh.WidthSegments.CurrentValue = Math.Max(WidthSegments, 1);
            _mesh.HeightSegments.CurrentValue = Math.Max(HeightSegments, 1);

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
