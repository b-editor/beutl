using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

[Display(Name = nameof(Strings.Line), ResourceType = typeof(Strings))]
public sealed partial class LineSegment : PathSegment
{
    public LineSegment()
    {
        ScanProperties<LineSegment>();
    }

    public LineSegment(Point point) : this()
    {
        Point.CurrentValue = point;
    }

    public LineSegment(float x, float y)
        : this(new Point(x, y))
    {
    }

    [Display(Name = nameof(Strings.Point), ResourceType = typeof(Strings))]
    public IProperty<Point> Point { get; } = Property.CreateAnimatable<Point>();

    public override void ApplyTo(IGeometryContext context, PathSegment.Resource resource)
    {
        var r = (Resource)resource;
        context.LineTo(r.Point);
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
    };
}
