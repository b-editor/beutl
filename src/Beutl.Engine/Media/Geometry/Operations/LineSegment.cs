using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

[Display(Name = nameof(GraphicsStrings.LineSegment), ResourceType = typeof(GraphicsStrings))]
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

    [Display(Name = nameof(GraphicsStrings.LineSegment_Point), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Point> Point { get; } = Property.CreateAnimatable<Point>();

    public override IProperty<Point> GetEndPoint()
    {
        return Point;
    }

    public partial class Resource
    {
        public override void ApplyTo(IGeometryContext context)
        {
            context.LineTo(Point);
        }

        public override Point? GetEndPoint()
        {
            return Point;
        }
    };
}
