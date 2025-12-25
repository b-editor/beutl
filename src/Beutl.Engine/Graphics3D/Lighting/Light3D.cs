using System.ComponentModel.DataAnnotations;
using System.Numerics;
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
    [Range(0f, float.MaxValue)]
    public IProperty<float> Intensity { get; } = Property.CreateAnimatable(1f);

    /// <summary>
    /// Gets whether this light is enabled.
    /// </summary>
    public IProperty<bool> IsLightEnabled { get; } = Property.CreateAnimatable(true);
}
