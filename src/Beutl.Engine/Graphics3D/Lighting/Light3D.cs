using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Media;

namespace Beutl.Graphics3D.Lighting;

/// <summary>
/// Base class for 3D lights.
/// </summary>
public abstract partial class Light3D : EngineObject
{
    public Light3D()
    {
        ScanProperties<Light3D>();
    }

    /// <summary>
    /// Gets the color of the light.
    /// </summary>
    [Display(Name = nameof(Strings.Color), ResourceType = typeof(Strings))]
    public IProperty<Color> Color { get; } = Property.CreateAnimatable(Colors.White);

    /// <summary>
    /// Gets the intensity of the light.
    /// </summary>
    [Display(Name = nameof(Strings.Intensity), ResourceType = typeof(Strings))]
    [Range(0f, float.MaxValue)]
    public IProperty<float> Intensity { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets whether this light casts shadows.
    /// </summary>
    [Display(Name = nameof(Strings.CastsShadow), ResourceType = typeof(Strings))]
    public IProperty<bool> CastsShadow { get; } = Property.CreateAnimatable(false);

    /// <summary>
    /// Gets the depth bias for shadow mapping to prevent shadow acne.
    /// </summary>
    [Display(Name = nameof(Strings.ShadowBias), ResourceType = typeof(Strings))]
    [Range(0f, 0.01f)]
    public IProperty<float> ShadowBias { get; } = Property.CreateAnimatable(0.0001f);

    /// <summary>
    /// Gets the normal bias for shadow mapping to prevent shadow acne on surfaces facing away from the light.
    /// </summary>
    [Display(Name = nameof(Strings.ShadowNormalBias), ResourceType = typeof(Strings))]
    [Range(0f, 0.1f)]
    public IProperty<float> ShadowNormalBias { get; } = Property.CreateAnimatable(0.02f);

    /// <summary>
    /// Gets the shadow strength (0 = no shadow, 1 = full shadow).
    /// </summary>
    [Display(Name = nameof(Strings.ShadowStrength), ResourceType = typeof(Strings))]
    [Range(0f, 1f)]
    public IProperty<float> ShadowStrength { get; } = Property.CreateAnimatable(1f);
}
