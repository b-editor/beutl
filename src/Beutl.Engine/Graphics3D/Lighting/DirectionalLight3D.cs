using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics3D.Lighting;

/// <summary>
/// A directional light that simulates sunlight.
/// </summary>
[Display(Name = nameof(GraphicsStrings.DirectionalLight3D), ResourceType = typeof(GraphicsStrings))]
public partial class DirectionalLight3D : Light3D
{
    public DirectionalLight3D()
    {
        ScanProperties<DirectionalLight3D>();
    }

    /// <summary>
    /// Gets the direction of the light. This is the direction the light is pointing,
    /// not where it's coming from.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Direction), ResourceType = typeof(GraphicsStrings))]
    [NumberStep(1, 0.1)]
    public IProperty<Vector3> Direction { get; } = Property.CreateAnimatable(new Vector3(0.5f, 1, 1));

    /// <summary>
    /// Gets the normalized light direction for use in shaders.
    /// </summary>
    public Vector3 GetNormalizedDirection(Resource resource)
    {
        var dir = resource.Direction;
        return dir == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(dir);
    }

    /// <summary>
    /// Gets the maximum distance from the camera at which shadows are rendered.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.DirectionalLight3D_ShadowDistance), ResourceType = typeof(GraphicsStrings))]
    [Range(1f, 100000f), NumberStep(10, 1)]
    public IProperty<float> ShadowDistance { get; } = Property.CreateAnimatable(5000f);

    /// <summary>
    /// Gets the size of the shadow map frustum (orthographic projection width/height).
    /// Larger values cover more area but reduce shadow quality.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.DirectionalLight3D_ShadowMapSize), ResourceType = typeof(GraphicsStrings))]
    [Range(1f, 50000f), NumberStep(10, 1)]
    public IProperty<float> ShadowMapSize { get; } = Property.CreateAnimatable(2000f);
}
