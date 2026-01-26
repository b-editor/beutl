using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

[Display(Name = nameof(Strings.Conic), ResourceType = typeof(Strings))]
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

    [Display(Name = nameof(Strings.ControlPoint), ResourceType = typeof(Strings))]
    public IProperty<Point> ControlPoint { get; } = Property.CreateAnimatable<Point>();

    [Display(Name = nameof(Strings.EndPoint), ResourceType = typeof(Strings))]
    public IProperty<Point> EndPoint { get; } = Property.CreateAnimatable<Point>();

    [Display(Name = nameof(Strings.Weight), ResourceType = typeof(Strings))]
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
