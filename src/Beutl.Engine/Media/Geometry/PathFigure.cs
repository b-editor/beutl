using System.ComponentModel.DataAnnotations;
using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Serialization;

namespace Beutl.Media;

[Display(Name = nameof(Strings.Figure), ResourceType = typeof(Strings))]
public sealed partial class PathFigure : EngineObject
{
    public PathFigure()
    {
        ScanProperties<PathFigure>();
    }

    public IProperty<bool> IsClosed { get; } = Property.CreateAnimatable<bool>();

    public IProperty<Point> StartPoint { get; } = Property.CreateAnimatable(new Point(float.NaN, float.NaN));

    public IListProperty<PathSegment> Segments { get; } = Property.CreateList<PathSegment>();

    public void ApplyTo(IGeometryContext context, Resource resource)
    {
        bool skipFirst = false;
        if (!resource.StartPoint.IsInvalid)
        {
            context.MoveTo(resource.StartPoint);
        }
        else if (resource.Segments.Count > 0)
        {
            if (resource.IsClosed)
            {
                var endPoint = resource.Segments[^1].GetEndPoint();
                if (endPoint.HasValue)
                {
                    context.MoveTo(endPoint.Value);
                }
            }
            else
            {
                var endPoint = resource.Segments[0].GetEndPoint();
                if (endPoint.HasValue)
                {
                    context.MoveTo(endPoint.Value);
                    skipFirst = true;
                }
            }
        }

        foreach (PathSegment.Resource item in resource.Segments)
        {
            if (skipFirst)
            {
                skipFirst = false;
                continue;
            }

            item.GetOriginal().ApplyTo(context, item);
        }

        if (resource.IsClosed)
            context.Close();
    }
}
