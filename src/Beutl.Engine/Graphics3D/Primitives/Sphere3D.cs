using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Graphics3D.Meshes;
using Beutl.Language;

namespace Beutl.Graphics3D.Primitives;

/// <summary>
/// A 3D sphere primitive.
/// </summary>
[Display(Name = nameof(Strings.Sphere3D), ResourceType = typeof(Strings))]
public sealed partial class Sphere3D : Object3D
{
    public Sphere3D()
    {
        ScanProperties<Sphere3D>();
    }

    /// <summary>
    /// Gets the radius of the sphere.
    /// </summary>
    [Display(Name = nameof(Strings.Radius), ResourceType = typeof(Strings))]
    [Range(0.001f, float.MaxValue)]
    public IProperty<float> Radius { get; } = Property.CreateAnimatable(0.5f);

    /// <summary>
    /// Gets the number of horizontal segments (longitude).
    /// </summary>
    [Display(Name = nameof(Strings.Segments), ResourceType = typeof(Strings))]
    [Range(3, 128)]
    public IProperty<int> Segments { get; } = Property.CreateAnimatable(32);

    /// <summary>
    /// Gets the number of vertical rings (latitude).
    /// </summary>
    [Display(Name = nameof(Strings.Rings), ResourceType = typeof(Strings))]
    [Range(2, 128)]
    public IProperty<int> Rings { get; } = Property.CreateAnimatable(16);

    public partial class Resource
    {
        private readonly SphereMesh _mesh = new();
        private SphereMesh.Resource? _meshResource;

        partial void PostUpdate(Sphere3D obj, RenderContext context)
        {
            _mesh.Radius.CurrentValue = Math.Max(Radius, 0.001f);
            _mesh.Segments.CurrentValue = Math.Max(Segments, 3);
            _mesh.Rings.CurrentValue = Math.Max(Rings, 2);

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
