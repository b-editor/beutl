using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

[Display(Name = nameof(Strings.EllipticalArc), ResourceType = typeof(Strings))]
public sealed partial class ArcSegment : PathSegment
{
    public ArcSegment()
    {
        ScanProperties<ArcSegment>();
    }

    [Display(Name = nameof(Strings.Radius), ResourceType = typeof(Strings))]
    public IProperty<Size> Radius { get; } = Property.CreateAnimatable<Size>();

    [Display(Name = nameof(Strings.RotationAngle), ResourceType = typeof(Strings))]
    public IProperty<float> RotationAngle { get; } = Property.CreateAnimatable<float>(0);

    [Display(Name = nameof(Strings.IsLargeArc), ResourceType = typeof(Strings))]
    public IProperty<bool> IsLargeArc { get; } = Property.CreateAnimatable<bool>(false);

    [Display(Name = nameof(Strings.SweepClockwise), ResourceType = typeof(Strings))]
    public IProperty<bool> SweepClockwise { get; } = Property.CreateAnimatable<bool>(true);

    [Display(Name = nameof(Strings.Point), ResourceType = typeof(Strings))]
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
