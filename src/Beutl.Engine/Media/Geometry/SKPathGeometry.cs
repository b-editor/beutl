using Beutl.Graphics;

using SkiaSharp;

namespace Beutl.Media;

internal sealed class SKPathGeometry : Geometry
{
    private SKPath? _path;
    private bool _clone;

    public SKPathGeometry(SKPath path, bool clone)
    {
        SetSKPath(path, clone);
    }

    public SKPathGeometry()
    {
    }

    public void SetSKPath(SKPath? path, bool clone)
    {
        if (_clone && _path != null)
        {
            _path.Dispose();
            _path = null;
        }

        if (path != null)
        {
            _clone = clone;
            _path = clone ? new SKPath(path) : path;
        }

        RaiseInvalidated(new RenderInvalidatedEventArgs(this));
    }

    public override void ApplyTo(IGeometryContext context)
    {
        base.ApplyTo(context);
        if (_path == null) return;

        if (context is GeometryContext typed)
        {
            typed.NativeObject.AddPath(_path);
        }
        else
        {
            using SKPath.RawIterator it = _path.CreateRawIterator();
            Span<SKPoint> points = stackalloc SKPoint[4];
            SKPathVerb pathVerb;

            do
            {
                pathVerb = it.Next(points);
                switch (pathVerb)
                {
                    case SKPathVerb.Move:
                        context.MoveTo(points[0].ToGraphicsPoint());
                        break;
                    case SKPathVerb.Line:
                        context.LineTo(points[1].ToGraphicsPoint());
                        break;
                    case SKPathVerb.Quad:
                        context.QuadraticTo(points[1].ToGraphicsPoint(), points[2].ToGraphicsPoint());
                        break;
                    case SKPathVerb.Conic:
                        context.ConicTo(points[1].ToGraphicsPoint(), points[2].ToGraphicsPoint(), it.ConicWeight());
                        break;
                    case SKPathVerb.Cubic:
                        context.CubicTo(points[1].ToGraphicsPoint(), points[2].ToGraphicsPoint(), points[3].ToGraphicsPoint());
                        break;
                    case SKPathVerb.Close:
                        context.Close();
                        break;
                    case SKPathVerb.Done:
                    default:
                        break;
                }
            } while (pathVerb != SKPathVerb.Done);

        }
    }
}
