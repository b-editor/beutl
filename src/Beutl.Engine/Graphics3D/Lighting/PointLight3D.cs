using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics3D.Lighting;

/// <summary>
/// A point light that emits light in all directions from a position.
/// </summary>
public partial class PointLight3D : Light3D
{
    public PointLight3D()
    {
        ScanProperties<PointLight3D>();
    }

    /// <summary>
    /// Gets the position of the light in world space.
    /// </summary>
    [Display(Name = nameof(Strings.Position), ResourceType = typeof(Strings))]
    public IProperty<Vector3> Position { get; } = Property.CreateAnimatable(Vector3.Zero);

    /// <summary>
    /// Gets the constant attenuation factor.
    /// Attenuation = 1.0 / (Constant + Linear * d + Quadratic * d^2)
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
    /// Objects beyond this distance will not be lit.
    /// </summary>
    [Range(0f, float.MaxValue)]
    public IProperty<float> Range { get; } = Property.CreateAnimatable(50f);
}
