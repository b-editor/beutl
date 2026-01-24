using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

[Display(Name = nameof(Strings.QuadraticBezierCurve), ResourceType = typeof(Strings))]
public sealed partial class QuadraticBezierSegment : PathSegment
{
    public QuadraticBezierSegment()
    {
        ScanProperties<QuadraticBezierSegment>();
    }

    public QuadraticBezierSegment(Point controlPoint, Point endPoint) : this()
    {
        ControlPoint.CurrentValue = controlPoint;
        EndPoint.CurrentValue = endPoint;
    }

    public IProperty<Point> ControlPoint { get; } = Property.CreateAnimatable<Point>();

    public IProperty<Point> EndPoint { get; } = Property.CreateAnimatable<Point>();

    public override void ApplyTo(IGeometryContext context, PathSegment.Resource resource)
    {
        var r = (Resource)resource;
        context.QuadraticTo(r.ControlPoint, r.EndPoint);
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
