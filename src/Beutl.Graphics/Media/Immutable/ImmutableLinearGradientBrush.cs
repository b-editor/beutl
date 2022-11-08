
using Beutl.Graphics;

namespace Beutl.Media.Immutable;

public class ImmutableLinearGradientBrush : ImmutableGradientBrush, ILinearGradientBrush
{
    public ImmutableLinearGradientBrush(
        IReadOnlyList<ImmutableGradientStop> gradientStops,
        float opacity = 1,
        ImmutableTransform? transform = null,
        RelativePoint? transformOrigin = null,
        GradientSpreadMethod spreadMethod = GradientSpreadMethod.Pad,
        RelativePoint? startPoint = null,
        RelativePoint? endPoint = null)
        : base(gradientStops, opacity, transform, transformOrigin, spreadMethod)
    {
        StartPoint = startPoint ?? RelativePoint.TopLeft;
        EndPoint = endPoint ?? RelativePoint.BottomRight;
    }

    public ImmutableLinearGradientBrush(LinearGradientBrush source)
        : base(source)
    {
        StartPoint = source.StartPoint;
        EndPoint = source.EndPoint;
    }

    public RelativePoint StartPoint { get; }

    public RelativePoint EndPoint { get; }
}
