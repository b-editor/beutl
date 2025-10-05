using System.ComponentModel.DataAnnotations;

using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media.Immutable;

namespace Beutl.Media;

/// <summary>
/// Describes how an area is painted.
/// </summary>
public abstract partial class Brush : EngineObject
{
    public Brush()
    {
        ScanProperties<Brush>();
    }

    /// <summary>
    /// Gets or sets the opacity of the brush.
    /// </summary>
    [Display(Name = nameof(Strings.Opacity), ResourceType = typeof(Strings))]
    [Range(0, 100)]
    public IProperty<float> Opacity { get; } = Property.CreateAnimatable(100f);

    /// <summary>
    /// Gets or sets the transform of the brush.
    /// </summary>
    [Display(Name = nameof(Strings.Transform), ResourceType = typeof(Strings))]
    public IProperty<Transform?> Transform { get; } = Property.Create<Transform?>();

    /// <summary>
    /// Gets or sets the origin of the brush <see cref="Transform"/>
    /// </summary>
    [Display(Name = nameof(Strings.TransformOrigin), ResourceType = typeof(Strings))]
    public IProperty<RelativePoint> TransformOrigin { get; } = Property.CreateAnimatable(RelativePoint.Center);
}
