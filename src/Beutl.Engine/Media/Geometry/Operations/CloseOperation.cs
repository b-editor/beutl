using Beutl.Graphics;

namespace Beutl.Media;

[Obsolete("Use 'PathGeometry.IsClosed'.")]
public sealed class CloseOperation : PathSegment
{
    public override void ApplyTo(IGeometryContext context)
    {
        context.Close();
    }

    public override CoreProperty<Point> GetEndPointProperty() => throw new NotSupportedException();
}
