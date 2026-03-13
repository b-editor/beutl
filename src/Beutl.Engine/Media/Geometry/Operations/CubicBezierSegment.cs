using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

[Display(Name = nameof(GraphicsStrings.CubicBezierSegment), ResourceType = typeof(GraphicsStrings))]
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

    [Display(Name = nameof(GraphicsStrings.CubicBezierSegment_ControlPoint1), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Point> ControlPoint1 { get; } = Property.CreateAnimatable<Point>();

    [Display(Name = nameof(GraphicsStrings.CubicBezierSegment_ControlPoint2), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Point> ControlPoint2 { get; } = Property.CreateAnimatable<Point>();

    [Display(Name = nameof(GraphicsStrings.EndPoint), ResourceType = typeof(GraphicsStrings))]
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
