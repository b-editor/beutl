using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using Beutl.Media.Immutable;

namespace Beutl.Media;

/// <summary>
/// Paints an area with a swept circular gradient.
/// </summary>
public sealed class ConicGradientBrush : GradientBrush
{
    public static readonly CoreProperty<RelativePoint> CenterProperty;
    public static readonly CoreProperty<float> AngleProperty;
    private RelativePoint _center = RelativePoint.Center;
    private float _angle;

    static ConicGradientBrush()
    {
        CenterProperty = ConfigureProperty<RelativePoint, ConicGradientBrush>(nameof(Center))
            .Accessor(o => o.Center, (o, v) => o.Center = v)
            .DefaultValue(RelativePoint.Center)
            .Register();

        AngleProperty = ConfigureProperty<float, ConicGradientBrush>(nameof(Angle))
            .Accessor(o => o.Angle, (o, v) => o.Angle = v)
            .DefaultValue(0)
            .Register();

        AffectsRender<ConicGradientBrush>(CenterProperty, AngleProperty);
    }

    /// <summary>
    /// Gets or sets the center point of the gradient.
    /// </summary>
    [Display(Name = nameof(Strings.Center), ResourceType = typeof(Strings))]
    public IProperty<RelativePoint> Center { get; } = Property.CreateAnimatable(RelativePoint.Center);

    /// <summary>
    /// Gets or sets the angle of the start and end of the sweep, measured from above the center point.
    /// </summary>
    [Display(Name = nameof(Strings.Angle), ResourceType = typeof(Strings))]
    public IProperty<float> Angle { get; } = Property.CreateAnimatable(0f);

    /// <inheritdoc/>
    public override BrushResource ToResource(RenderContext context)
    {
        return new ConicGradientBrushResource(
            context.Get(Center),
            context.Get(Angle),
            GetGradientStopsResource(context),
        );

    }
}
