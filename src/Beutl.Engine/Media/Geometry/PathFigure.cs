using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Serialization;

namespace Beutl.Media;

[Display(Name = nameof(GraphicsStrings.PathFigure), ResourceType = typeof(GraphicsStrings))]
public sealed partial class PathFigure : EngineObject
{
    public PathFigure()
    {
        ScanProperties<PathFigure>();
    }

    [Display(Name = nameof(GraphicsStrings.PathFigure_IsClosed), ResourceType = typeof(GraphicsStrings))]
    public IProperty<bool> IsClosed { get; } = Property.CreateAnimatable<bool>();

    [Display(Name = nameof(GraphicsStrings.PathFigure_StartPoint), ResourceType = typeof(GraphicsStrings))]
    public IProperty<Point> StartPoint { get; } = Property.CreateAnimatable(new Point(float.NaN, float.NaN));

    public IListProperty<PathSegment> Segments { get; } = Property.CreateList<PathSegment>();

    public partial class Resource
    {
        public void ApplyTo(IGeometryContext context)
        {
            bool skipFirst = false;
            if (!StartPoint.IsInvalid)
            {
                context.MoveTo(StartPoint);
            }
            else if (Segments.Count > 0)
            {
                if (IsClosed)
                {
                    var endPoint = Segments[^1].GetEndPoint();
                    if (endPoint.HasValue)
                    {
                        context.MoveTo(endPoint.Value);
                    }
                }
                else
                {
                    var endPoint = Segments[0].GetEndPoint();
                    if (endPoint.HasValue)
                    {
                        context.MoveTo(endPoint.Value);
                        skipFirst = true;
                    }
                }
            }

            foreach (PathSegment.Resource item in Segments)
            {
                if (skipFirst)
                {
                    skipFirst = false;
                    continue;
                }

                item.ApplyTo(context);
            }

            if (IsClosed)
                context.Close();
        }
    }
}
