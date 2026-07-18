using System.ComponentModel.DataAnnotations;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics3D.Meshes;
using Beutl.Language;

namespace Beutl.Graphics3D.Primitives;

/// <summary>
/// A 3D cube primitive.
/// </summary>
[Display(Name = nameof(GraphicsStrings.Cube3D), ResourceType = typeof(GraphicsStrings))]
public sealed partial class Cube3D : Object3D
{
    public Cube3D()
    {
        ScanProperties<Cube3D>();
    }

    /// <summary>
    /// Gets the width of the cube (X-axis).
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Width), ResourceType = typeof(GraphicsStrings))]
    [Range(0.001f, float.MaxValue), NumberStep(0.1, 0.01)]
    public IProperty<float> Width { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the height of the cube (Y-axis).
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Height), ResourceType = typeof(GraphicsStrings))]
    [Range(0.001f, float.MaxValue), NumberStep(0.1, 0.01)]
    public IProperty<float> Height { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets the depth of the cube (Z-axis).
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Depth), ResourceType = typeof(GraphicsStrings))]
    [Range(0.001f, float.MaxValue), NumberStep(0.1, 0.01)]
    public IProperty<float> Depth { get; } = Property.CreateAnimatable(1f);

    public partial class Resource
    {
        private readonly CubeMesh _mesh = new();
        private CubeMesh.Resource? _meshResource;

        partial void PostUpdate(Cube3D obj, CompositionContext context)
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
                    CubeMesh.Resource replacement = _mesh.ToResource(context);
                    var cleanupFailure = ReplaceOwnedResource(ref _meshResource, replacement);
                    Version++;
                    cleanupFailure?.Throw();
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

        partial void PrepareResourceDispose(
            bool disposing,
            EngineObject.Resource.GeneratedResourceCleanupContext context)
        {
            if (disposing)
                context.Reserve(_meshResource);
        }

        partial void PostDispose(bool disposing)
        {
            if (!disposing)
                return;

            _meshResource = null;
        }

        public override Mesh.Resource? GetMesh() => ReadGeneratedResourceState(ref _meshResource);
    }
}
