using Beutl.Engine;
using Beutl.Graphics;

namespace Beutl.Media;

public sealed partial class ArcSegment : PathSegment
{
    public ArcSegment()
    {
        ScanProperties<ArcSegment>();
    }

    public IProperty<Size> Radius { get; } = Property.CreateAnimatable<Size>();

    public IProperty<float> RotationAngle { get; } = Property.CreateAnimatable<float>(0);

    public IProperty<bool> IsLargeArc { get; } = Property.CreateAnimatable<bool>(false);

    public IProperty<bool> SweepClockwise { get; } = Property.CreateAnimatable<bool>(true);

    public IProperty<Point> Point { get; } = Property.CreateAnimatable<Point>();

    public override void ApplyTo(IGeometryContext context, PathSegment.Resource resource)
    {
        var r = (Resource)resource;
        context.ArcTo(r.Radius, r.RotationAngle, r.IsLargeArc, r.SweepClockwise, r.Point);
    }

    public override IProperty<Point> GetEndPoint()
    {
        return Point;
    }

    public partial class Resource
    {
        public override Point? GetEndPoint()
        {
            return Point;
        }
    }
}
