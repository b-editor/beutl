using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;

namespace Beutl.Editor.Components.PathEditorTab.Services;

internal enum PathPointPropertyRole { Position, Incoming, Outgoing, Shared }

internal readonly record struct PathPointProperty(PathSegment Owner, IProperty<Point> Property, PathPointPropertyRole Role);

internal static class PathPointProperties
{
    // A segment stores its end anchor, its incoming handle, and (for cubics) the
    // preceding anchor's outgoing handle. The inspector follows the anchor instead.
    public static PathPointProperty[] Get(PathFigure figure, PathSegment anchor, CompositionContext context)
    {
        int index = figure.Segments.IndexOf(anchor);
        if (index < 0) return [];
        var result = new List<PathPointProperty>(3)
        {
            new(anchor, anchor.GetEndPoint(), PathPointPropertyRole.Position)
        };
        bool closed = figure.IsClosed.GetValue(context);
        bool explicitStart = !figure.StartPoint.GetValue(context).IsInvalid;
        if (index > 0 || closed || explicitStart)
        {
            IProperty<Point>? incoming = anchor switch
            {
                CubicBezierSegment cubic => cubic.ControlPoint2,
                QuadraticBezierSegment quadratic => quadratic.ControlPoint,
                ConicSegment conic => conic.ControlPoint,
                _ => null
            };
            if (incoming != null) result.Add(new(anchor, incoming, PathPointPropertyRole.Incoming));
        }

        int nextIndex = index + 1;
        // With an explicit StartPoint, Close() adds a straight closing edge; the
        // first segment's handle belongs to that distinct start point, not the last anchor.
        if (nextIndex == figure.Segments.Count && closed && !explicitStart) nextIndex = 0;
        if (nextIndex < figure.Segments.Count)
        {
            PathSegment next = figure.Segments[nextIndex];
            IProperty<Point>? outgoing = next switch
            {
                CubicBezierSegment cubic => cubic.ControlPoint1,
                QuadraticBezierSegment quadratic => quadratic.ControlPoint,
                ConicSegment conic => conic.ControlPoint,
                _ => null
            };
            if (outgoing != null)
            {
                int duplicate = result.FindIndex(p => ReferenceEquals(p.Property, outgoing));
                if (duplicate >= 0) result[duplicate] = result[duplicate] with { Role = PathPointPropertyRole.Shared };
                else result.Add(new(next, outgoing, PathPointPropertyRole.Outgoing));
            }
        }
        return result.ToArray();
    }
}
