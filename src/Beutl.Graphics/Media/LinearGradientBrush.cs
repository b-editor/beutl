using Beutl.Graphics;
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
            .DefaultValue(RelativePoint.TopLeft)
            .Accessor(o => o.StartPoint, (o, v) => o.StartPoint = v)
            .PropertyFlags(PropertyFlags.All)
            .SerializeName("start-point")
            .Register();

        EndPointProperty = ConfigureProperty<RelativePoint, LinearGradientBrush>(nameof(EndPoint))
            .DefaultValue(RelativePoint.BottomRight)
            .Accessor(o => o.EndPoint, (o, v) => o.EndPoint = v)
            .PropertyFlags(PropertyFlags.All)
            .SerializeName("end-point")
            .Register();

        AffectsRender<LinearGradientBrush>(StartPointProperty, EndPointProperty);
    }

    /// <summary>
    /// Gets or sets the start point for the gradient.
    /// </summary>
    public RelativePoint StartPoint
    {
        get => _startPoint;
        set => SetAndRaise(StartPointProperty, ref _startPoint, value);
    }

    /// <summary>
    /// Gets or sets the end point for the gradient.
    /// </summary>
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
