using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;

using Beutl;
using Beutl.Media;

using SkiaSharp;

namespace PathEditor;

public class PathGeometryControl : Control
{
    private readonly PathDrawOperation _op;

    public PathGeometryControl(PathGeometry geometry)
    {
        _op = new(geometry);
        geometry.Invalidated += OnGeometryInvalidated;
    }

    private void OnGeometryInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        InvalidateVisual();
        InvalidateMeasure();
        InvalidateArrange();
    }

    public override void Render(Avalonia.Media.DrawingContext context)
    {
        base.Render(context);
        context.Custom(_op);
    }

    private class PathDrawOperation(PathGeometry geometry) : ICustomDrawOperation
    {
        public Rect Bounds => Geometry.Bounds.ToAvaRect();

        public PathGeometry Geometry { get; } = geometry;

        public void Dispose()
        {
        }

        public bool Equals(ICustomDrawOperation? other)
        {
            return other is PathDrawOperation o
                && o.Geometry == Geometry
                && o.Geometry.GetVersion() == Geometry.GetVersion();
        }

        public bool HitTest(Point p)
        {
            return false;
        }

        public void Render(Avalonia.Media.ImmediateDrawingContext context)
        {
            if (context.TryGetFeature<ISkiaSharpApiLeaseFeature>(out var sk))
            {
                using (var skapi = sk.Lease())
                using (var paint = new SKPaint())
                {
                    paint.Color = SKColors.LightGray;
                    paint.Style = SKPaintStyle.Fill;
                    paint.HintingLevel = SKPaintHinting.Full;
                    paint.FilterQuality = SKFilterQuality.High;
                    paint.IsAntialias = true;

                    SKPath path = Geometry.GetNativeObjectPublic();
                    skapi.SkCanvas.DrawPath(path, paint);

                    paint.Color = SKColors.SkyBlue;
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 10;
                    skapi.SkCanvas.DrawPath(path, paint);

                    paint.StrokeWidth = 1;
                    paint.Color = SKColors.Black;
                    using (var dash = SKPathEffect.CreateDash([3, 2], 0))
                    {
                        paint.PathEffect = dash;
                    }

                    Beutl.Graphics.Point lastPoint = default;
                    foreach (PathOperation item in Geometry.Operations)
                    {
                        switch (item)
                        {
                            case ArcOperation arc:
                                lastPoint = arc.Point;
                                break;

                            case CubicBezierOperation cubic:
                                skapi.SkCanvas.DrawLine(
                                    lastPoint.X, lastPoint.Y,
                                    cubic.ControlPoint1.X, cubic.ControlPoint1.Y,
                                    paint);

                                lastPoint = cubic.EndPoint;

                                skapi.SkCanvas.DrawLine(
                                    lastPoint.X, lastPoint.Y,
                                    cubic.ControlPoint2.X, cubic.ControlPoint2.Y,
                                    paint);
                                break;

                            case LineOperation line:
                                lastPoint = line.Point;
                                break;

                            case MoveOperation move:
                                lastPoint = move.Point;
                                break;

                            case QuadraticBezierOperation quad:
                                skapi.SkCanvas.DrawLine(
                                    lastPoint.X, lastPoint.Y,
                                    quad.ControlPoint.X, quad.ControlPoint.Y,
                                    paint);

                                lastPoint = quad.EndPoint;

                                skapi.SkCanvas.DrawLine(
                                    lastPoint.X, lastPoint.Y,
                                    quad.ControlPoint.X, quad.ControlPoint.Y,
                                    paint);
                                break;

                            case CloseOperation:
                            default:
                                lastPoint = default;
                                break;
                        }
                    }
                }
            }
        }
    }
}
