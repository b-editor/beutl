using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

[Display(Name = nameof(GraphicsStrings.ArcSegment), ResourceType = typeof(GraphicsStrings))]
public sealed partial class ArcSegment : PathSegment
{
    public ArcSegment()
    {
        ScanProperties<ArcSegment>();
    }

    [Display(Name = nameof(GraphicsStrings.ArcSegment_Radius), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Size> Radius { get; } = Property.CreateAnimatable<Size>();

    [Display(Name = nameof(GraphicsStrings.ArcSegment_RotationAngle), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> RotationAngle { get; } = Property.CreateAnimatable<float>(0);

    [Display(Name = nameof(GraphicsStrings.ArcSegment_IsLargeArc), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> IsLargeArc { get; } = Property.CreateAnimatable<bool>(false);

    [Display(Name = nameof(GraphicsStrings.ArcSegment_SweepClockwise), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> SweepClockwise { get; } = Property.CreateAnimatable<bool>(true);

    [Display(Name = nameof(GraphicsStrings.ArcSegment_Point), ResourceType = typeof(GraphicsStrings))]
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
