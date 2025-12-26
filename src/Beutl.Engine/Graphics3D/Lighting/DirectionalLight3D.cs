using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;

namespace Beutl.Graphics3D.Lighting;

/// <summary>
/// A directional light that simulates sunlight.
/// </summary>
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
    public IProperty<Vector3> Direction { get; } = Property.CreateAnimatable(new Vector3(0, -1, 0));

    /// <summary>
    /// Gets the normalized light direction for use in shaders.
    /// </summary>
    public Vector3 GetNormalizedDirection(Resource resource)
    {
        var dir = resource.Direction;
        return dir == Vector3.Zero ? new Vector3(0, -1, 0) : Vector3.Normalize(dir);
    }

    /// <summary>
    /// Gets the maximum distance from the camera at which shadows are rendered.
    /// </summary>
    [Range(1f, 1000f)]
    public IProperty<float> ShadowDistance { get; } = Property.CreateAnimatable(50f);

    /// <summary>
    /// Gets the size of the shadow map frustum (orthographic projection width/height).
    /// Larger values cover more area but reduce shadow quality.
    /// </summary>
    [Range(1f, 500f)]
    public IProperty<float> ShadowMapSize { get; } = Property.CreateAnimatable(20f);
}
