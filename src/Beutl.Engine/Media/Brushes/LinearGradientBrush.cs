using System.ComponentModel.DataAnnotations;

using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.Media.Immutable;

namespace Beutl.Media;

/// <summary>
/// A brush that draws with a linear gradient.
/// </summary>
public sealed class LinearGradientBrush : GradientBrush, ILinearGradientBrush
{
    public static readonly CoreProperty<RelativePoint> StartPointProperty;
    public static readonly CoreProperty<RelativePoint> EndPointProperty;
    private RelativePoint _startPoint = RelativePoint.TopLeft;
    private RelativePoint _endPoint = RelativePoint.BottomRight;

    static LinearGradientBrush()
    {
        StartPointProperty = ConfigureProperty<RelativePoint, LinearGradientBrush>(nameof(StartPoint))
            .Accessor(o => o.StartPoint, (o, v) => o.StartPoint = v)
            .DefaultValue(RelativePoint.TopLeft)
            .Register();

        EndPointProperty = ConfigureProperty<RelativePoint, LinearGradientBrush>(nameof(EndPoint))
            .Accessor(o => o.EndPoint, (o, v) => o.EndPoint = v)
            .DefaultValue(RelativePoint.BottomRight)
            .Register();

        AffectsRender<LinearGradientBrush>(StartPointProperty, EndPointProperty);
    }

    /// <summary>
    /// Gets or sets the start point for the gradient.
    /// </summary>
    [Display(Name = nameof(Strings.StartPoint), ResourceType = typeof(Strings))]
    public RelativePoint StartPoint
    {
        get => _startPoint;
        set => SetAndRaise(StartPointProperty, ref _startPoint, value);
    }

    /// <summary>
    /// Gets or sets the end point for the gradient.
    /// </summary>
    [Display(Name = nameof(Strings.EndPoint), ResourceType = typeof(Strings))]
    public RelativePoint EndPoint
    {
        get => _endPoint;
        set => SetAndRaise(EndPointProperty, ref _endPoint, value);
    }

    /// <inheritdoc/>
    public override IBrush ToImmutable()
    {
        return new ImmutableLinearGradientBrush(this);
    }
}
