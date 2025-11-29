using Beutl.Engine;
using Beutl.Graphics;

namespace Beutl.Media;

public sealed partial class ConicSegment : PathSegment
{
    public ConicSegment()
    {
        ScanProperties<ConicSegment>();
    }

    public ConicSegment(Point controlPoint, Point endPoint, float weight) : this()
    {
        ControlPoint.CurrentValue = controlPoint;
        EndPoint.CurrentValue = endPoint;
        Weight.CurrentValue = weight;
    }

    public IProperty<Point> ControlPoint { get; } = Property.CreateAnimatable<Point>();

    public IProperty<Point> EndPoint { get; } = Property.CreateAnimatable<Point>();

    public IProperty<float> Weight { get; } = Property.CreateAnimatable<float>(1);

    public override void ApplyTo(IGeometryContext context, PathSegment.Resource resource)
    {
        var r = (Resource)resource;
        context.ConicTo(r.ControlPoint, r.EndPoint, r.Weight);
    }

    public override IProperty<Point> GetEndPoint()
    {
        return EndPoint;
    }

    public partial class Resource
    {
        public override Point? GetEndPoint()
        {
            return EndPoint;
        }
    }
}
