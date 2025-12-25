using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics3D.Lighting;

/// <summary>
/// A spotlight that emits light in a cone from a position.
/// </summary>
public partial class SpotLight3D : Light3D
{
    public SpotLight3D()
    {
        ScanProperties<SpotLight3D>();
    }

    /// <summary>
    /// Gets the position of the light in world space.
    /// </summary>
    [Display(Name = nameof(Strings.Position), ResourceType = typeof(Strings))]
    public IProperty<Vector3> Position { get; } = Property.CreateAnimatable(new Vector3(0, 5, 0));

    /// <summary>
    /// Gets the direction the spotlight is pointing.
    /// </summary>
    public IProperty<Vector3> Direction { get; } = Property.CreateAnimatable(new Vector3(0, -1, 0));

    /// <summary>
    /// Gets the inner cone angle in degrees.
    /// Within this angle, the light is at full intensity.
    /// </summary>
    [Range(0f, 90f)]
    public IProperty<float> InnerConeAngle { get; } = Property.CreateAnimatable(12.5f);

    /// <summary>
    /// Gets the outer cone angle in degrees.
    /// Between inner and outer cone, the light fades out.
    /// </summary>
    [Range(0f, 90f)]
    public IProperty<float> OuterConeAngle { get; } = Property.CreateAnimatable(17.5f);

    /// <summary>
    /// Gets the constant attenuation factor.
    /// </summary>
    [Range(0f, float.MaxValue)]
    public IProperty<float> ConstantAttenuation { get; } = Property.CreateAnimatable(1.0f);

    /// <summary>
    /// Gets the linear attenuation factor.
    /// </summary>
    [Range(0f, float.MaxValue)]
    public IProperty<float> LinearAttenuation { get; } = Property.CreateAnimatable(0.09f);

    /// <summary>
    /// Gets the quadratic attenuation factor.
    /// </summary>
    [Range(0f, float.MaxValue)]
    public IProperty<float> QuadraticAttenuation { get; } = Property.CreateAnimatable(0.032f);

    /// <summary>
    /// Gets the maximum range of the light.
    /// </summary>
    [Range(0f, float.MaxValue)]
    public IProperty<float> Range { get; } = Property.CreateAnimatable(50f);

    /// <summary>
    /// Gets the normalized direction for use in shaders.
    /// </summary>
    public Vector3 GetNormalizedDirection(Resource resource)
    {
        var dir = resource.Direction;
        return dir == Vector3.Zero ? new Vector3(0, -1, 0) : Vector3.Normalize(dir);
    }
}
