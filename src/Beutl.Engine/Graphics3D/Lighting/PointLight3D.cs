using System.ComponentModel.DataAnnotations;
using System.Numerics;
using Beutl.Engine;
using Beutl.Language;

namespace Beutl.Graphics3D.Lighting;

/// <summary>
/// A point light that emits light in all directions from a position.
/// </summary>
[Display(Name = nameof(GraphicsStrings.PointLight3D), ResourceType = typeof(GraphicsStrings))]
public partial class PointLight3D : Light3D
{
    public PointLight3D()
    {
        ScanProperties<PointLight3D>();
    }

    /// <summary>
    /// Gets the position of the light in world space.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.Position), ResourceType = typeof(GraphicsStrings))]
    [NumberStep(0.1, 0.01)]
    public IProperty<Vector3> Position { get; } = Property.CreateAnimatable(Vector3.Zero);

    /// <summary>
    /// Gets the constant attenuation factor.
    /// Attenuation = 1.0 / (Constant + Linear * d + Quadratic * d^2)
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.PointLight3D_ConstantAttenuation), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, float.MaxValue), NumberStep(0.1, 0.01)]
    public IProperty<float> ConstantAttenuation { get; } = Property.CreateAnimatable(1.0f);

    /// <summary>
    /// Gets the linear attenuation factor.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.PointLight3D_LinearAttenuation), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, float.MaxValue), NumberStep(0.1, 0.01)]
    public IProperty<float> LinearAttenuation { get; } = Property.CreateAnimatable(0.09f);

    /// <summary>
    /// Gets the quadratic attenuation factor.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.PointLight3D_QuadraticAttenuation), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, float.MaxValue), NumberStep(0.1, 0.01)]
    public IProperty<float> QuadraticAttenuation { get; } = Property.CreateAnimatable(0.032f);

    /// <summary>
    /// Gets the maximum range of the light.
    /// Objects beyond this distance will not be lit.
    /// </summary>
    [Display(Name = nameof(GraphicsStrings.PointLight3D_Range), ResourceType = typeof(GraphicsStrings))]
    [Range(0f, float.MaxValue), NumberStep(0.1, 0.01)]
    public IProperty<float> Range { get; } = Property.CreateAnimatable(50f);
}
