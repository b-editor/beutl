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
    public IExpressionProperty<bool> IsClosed { get; } = Property.CreateAnimatable<bool>();

    [Display(Name = nameof(GraphicsStrings.PathFigure_StartPoint), ResourceType = typeof(GraphicsStrings))]
    public IExpressionProperty<Point> StartPoint { get; } = Property.CreateAnimatable(new Point(float.NaN, float.NaN));

    public IListProperty<PathSegment> Segments { get; } = Property.CreateList<PathSegment>();

    public partial class Resource
    {
        public void ApplyTo(IGeometryContext context)
        {
            int skipIndex = -1;
            if (!StartPoint.IsInvalid)
            {
                context.MoveTo(StartPoint);
            }
            else if (Segments.Count > 0)
            {
                if (IsClosed)
                {
                    int last = IndexOfLoaded(fromEnd: true);
                    var endPoint = last >= 0 ? Segments[last].GetEndPoint() : null;
                    if (endPoint.HasValue)
                    {
                        context.MoveTo(endPoint.Value);
                    }
                }
                else
                {
                    int first = IndexOfLoaded(fromEnd: false);
                    var endPoint = first >= 0 ? Segments[first].GetEndPoint() : null;
                    if (endPoint.HasValue)
                    {
                        context.MoveTo(endPoint.Value);
                        skipIndex = first;
                    }
                }
            }

            for (int i = 0; i < Segments.Count; i++)
            {
                if (i != skipIndex)
                    Segments[i].ApplyTo(context);
            }

            if (IsClosed)
                context.Close();
        }

        // A segment whose type did not load adds nothing, so the implicit start comes from the segments that did.
        private int IndexOfLoaded(bool fromEnd)
        {
            for (int i = 0; i < Segments.Count; i++)
            {
                int index = fromEnd ? Segments.Count - 1 - i : i;
                if (Segments[index] is not FallbackPathSegment.Resource)
                    return index;
            }

            return -1;
        }
    }
}
