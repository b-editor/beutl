using System.ComponentModel.DataAnnotations;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Rendering;
using Beutl.Language;
using SkiaSharp;

namespace Beutl.Media;

[Display(Name = nameof(GraphicsStrings.PathGeometry), ResourceType = typeof(GraphicsStrings))]
public sealed partial class PathGeometry : Geometry
{
    public PathGeometry()
    {
        ScanProperties<PathGeometry>();
    }

    public IListProperty<PathFigure> Figures { get; } = Property.CreateList<PathFigure>();

    public static PathGeometry Parse(string svg)
    {
        using var path = SKPath.ParseSvgPathData(svg);
        using SKPath.RawIterator it = path.CreateRawIterator();

        var result = new PathGeometry();
        var currentFigure = new PathFigure();
        Span<SKPoint> points = stackalloc SKPoint[4];
        SKPathVerb pathVerb;

        do
        {
            pathVerb = it.Next(points);
            PathSegment? segment;

            if (pathVerb == SKPathVerb.Move)
            {
                if (!currentFigure.StartPoint.CurrentValue.IsInvalid || currentFigure.Segments.Count > 0)
                {
                    result.Figures.Add(currentFigure);
                    currentFigure = new PathFigure();
                }

                currentFigure.StartPoint.CurrentValue = points[0].ToGraphicsPoint();
            }
            else if (pathVerb == SKPathVerb.Close)
            {
                currentFigure.IsClosed.CurrentValue = true;
                result.Figures.Add(currentFigure);
                currentFigure = new PathFigure();
            }

            segment = pathVerb switch
            {
                SKPathVerb.Line => new LineSegment(points[1].ToGraphicsPoint()),
                SKPathVerb.Quad => new QuadraticBezierSegment(points[1].ToGraphicsPoint(), points[2].ToGraphicsPoint()),
                SKPathVerb.Conic => new ConicSegment(points[1].ToGraphicsPoint(), points[2].ToGraphicsPoint(), it.ConicWeight()),
                SKPathVerb.Cubic => new CubicBezierSegment(points[1].ToGraphicsPoint(), points[2].ToGraphicsPoint(), points[3].ToGraphicsPoint()),
                SKPathVerb.Done or _ => null,
            };

            if (segment != null)
            {
                currentFigure.Segments.Add(segment);
            }

        } while (pathVerb != SKPathVerb.Done);

        if (currentFigure.Segments.Count > 0)
            result.Figures.Add(currentFigure);

        return result;
    }

    public partial class Resource
    {
        public override void ApplyTo(IGeometryContext context)
        {
            foreach (PathFigure.Resource item in Figures)
            {
                item.ApplyTo(context);
            }
        }

        public PathFigure.Resource? HitTestFigure(Point point, Pen.Resource? pen)
        {
            Rect bounds = Bounds;

            foreach (PathFigure.Resource item in Figures)
            {
                using (var context = new GeometryContext())
                {
                    context.FillType = FillType;
                    item.ApplyTo(context);
                    if (Transform != null)
                    {
                        context.Transform(Transform.Matrix);
                    }

                    if (context.NativeObject.Contains(point.X, point.Y))
                    {
                        return item;
                    }

                    if (pen != null)
                    {
                        using SKPath strokePath = PenHelper.CreateStrokePath(context.NativeObject, pen, bounds);
                        if (strokePath.Contains(point.X, point.Y))
                        {
                            return item;
                        }
                    }

                }
            }

            return null;
        }
    }
}
