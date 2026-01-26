using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

[Display(Name = nameof(Strings.CubicBezierCurve), ResourceType = typeof(Strings))]
public sealed partial class CubicBezierSegment : PathSegment
{
    public CubicBezierSegment()
    {
        ScanProperties<CubicBezierSegment>();
    }

    public CubicBezierSegment(Point controlPoint1, Point controlPoint2, Point endPoint) : this()
    {
        ControlPoint1.CurrentValue = controlPoint1;
        ControlPoint2.CurrentValue = controlPoint2;
        EndPoint.CurrentValue = endPoint;
    }

    [Display(Name = nameof(Strings.ControlPoint1), ResourceType = typeof(Strings))]
    public IProperty<Point> ControlPoint1 { get; } = Property.CreateAnimatable<Point>();

    [Display(Name = nameof(Strings.ControlPoint2), ResourceType = typeof(Strings))]
    public IProperty<Point> ControlPoint2 { get; } = Property.CreateAnimatable<Point>();

    [Display(Name = nameof(Strings.EndPoint), ResourceType = typeof(Strings))]
    public IProperty<Point> EndPoint { get; } = Property.CreateAnimatable<Point>();

    public override void ApplyTo(IGeometryContext context, PathSegment.Resource resource)
    {
        var r = (Resource)resource;
        context.CubicTo(r.ControlPoint1, r.ControlPoint2, r.EndPoint);
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
