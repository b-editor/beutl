using Beutl.Graphics;

using SkiaSharp;

namespace Beutl.Media;

internal sealed partial class SKPathGeometry : Geometry
{
    public partial class Resource
    {
        private SKPath? _path;

        // Test hook: exposes the owned glyph path so deterministic disposal can be asserted.
        internal SKPath? Path => _path;

        // The resource owns _path in both clone modes (clone: true copies and owns; clone: false takes
        // ownership of the handed-off path), so it is always responsible for releasing it.
        public void SetSKPath(SKPath? path, bool clone)
        {
            SKPath? newPath = path != null && clone ? new SKPath(path) : path;
            if (!ReferenceEquals(_path, newPath))
            {
                _path?.Dispose();
            }

            _path = newPath;
            InvalidateCachedPaths();
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

        partial void PostDispose(bool disposing)
        {
            _path?.Dispose();
            _path = null;
        }
    }
}
