using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;

namespace Beutl.Media;

[Display(Name = nameof(GraphicsStrings.ConicSegment), ResourceType = typeof(GraphicsStrings))]
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

    [Display(Name = nameof(GraphicsStrings.ConicSegment_ControlPoint), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Point> ControlPoint { get; } = Property.CreateAnimatable<Point>();

    [Display(Name = nameof(GraphicsStrings.EndPoint), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Point> EndPoint { get; } = Property.CreateAnimatable<Point>();

    [Display(Name = nameof(GraphicsStrings.ConicSegment_Weight), ResourceType = typeof(GraphicsStrings))]
    public IProperty<float> Weight { get; } = Property.CreateAnimatable<float>(1);

    public override IProperty<Point> GetEndPoint()
    {
        return EndPoint;
    }

    public partial class Resource
    {
        public override void ApplyTo(IGeometryContext context)
        {
            context.ConicTo(ControlPoint, EndPoint, Weight);
        }

        public override Point? GetEndPoint()
        {
            return EndPoint;
        }
    }
}
