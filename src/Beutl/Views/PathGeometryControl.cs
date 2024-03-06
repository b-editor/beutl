using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;

using Beutl.Media;

using SkiaSharp;

namespace Beutl.Views;

public class PathGeometryControl : Control
{
    public static readonly StyledProperty<PathGeometry?> GeometryProperty =
        AvaloniaProperty.Register<PathGeometryControl, PathGeometry?>(nameof(Geometry));

    public static readonly StyledProperty<PathOperation?> SelectedOperationProperty =
        AvaloniaProperty.Register<PathGeometryControl, PathOperation?>(nameof(SelectedOperation));

    public static readonly StyledProperty<Matrix> MatrixProperty =
        AvaloniaProperty.Register<PathGeometryControl, Matrix>(nameof(Matrix), Matrix.Identity);

    public static readonly StyledProperty<double> ScaleProperty =
        AvaloniaProperty.Register<PathGeometryControl, double>(nameof(Scale), 1.0);

    static PathGeometryControl()
    {
        AffectsRender<PathGeometryControl>(GeometryProperty, MatrixProperty, ScaleProperty, SelectedOperationProperty);
    }

    public PathOperation? SelectedOperation
    {
        get => GetValue(SelectedOperationProperty);
        set => SetValue(SelectedOperationProperty, value);
    }

    public Matrix Matrix
    {
        get => GetValue(MatrixProperty);
        set => SetValue(MatrixProperty, value);
    }

    public PathGeometry? Geometry
    {
        get => GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }

    public double Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GeometryProperty)
        {
            if (change.OldValue is PathGeometry oldValue)
            {
                oldValue.Invalidated -= OnGeometryInvalidated;
            }

            if (change.NewValue is PathGeometry newValue)
            {
                newValue.Invalidated += OnGeometryInvalidated;
            }
        }
    }

    private void OnGeometryInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Dispatcher.UIThread.Post(InvalidateVisual);
    }

    public override void Render(Avalonia.Media.DrawingContext context)
    {
        base.Render(context);
        if (Geometry != null && SelectedOperation != null)
        {
            context.Custom(new PathDrawOperation(
                Geometry, Matrix, Geometry.Bounds.ToAvaRect(),
                Scale, Geometry.Operations.IndexOf(SelectedOperation)));
        }
    }

    private class PathDrawOperation(PathGeometry geometry, Matrix matrix, Rect bounds, double scale, int index) : ICustomDrawOperation
    {
        public Rect Bounds => bounds;

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
                using (var path = new SKPath(Geometry.GetNativeObjectPublic()))
                using (var skapi = sk.Lease())
                using (var paint = new SKPaint())
                {
                    path.Transform(matrix.ToSKMatrix().PostConcat(SKMatrix.CreateScale((float)(scale), (float)(scale))));

                    paint.HintingLevel = SKPaintHinting.Full;
                    paint.FilterQuality = SKFilterQuality.Low;
                    paint.IsAntialias = true;

                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 1;
                    paint.Color = SKColors.White;
                    using (var dash = SKPathEffect.CreateDash([3, 3], 0))
                    {
                        paint.PathEffect = dash;
                    }
                    using (var filter = SKImageFilter.CreateDropShadow(1, 1, 0, 0, SKColors.Black))
                    {
                        paint.ImageFilter = filter;
                    }

                    using SKPath.RawIterator it = path.CreateRawIterator();
                    Span<SKPoint> points = new SKPoint[4];
                    SKPathVerb pathVerb;
                    int count = -1;

                    do
                    {
                        pathVerb = it.Next(points);
                        if (index == -1 || count == index)
                        {
                            switch (pathVerb)
                            {
                                case SKPathVerb.Quad:
                                    skapi.SkCanvas.DrawLine(
                                       points[0],
                                       points[1],
                                       paint);

                                    skapi.SkCanvas.DrawLine(
                                        points[2],
                                        points[1],
                                        paint);
                                    break;
                                case SKPathVerb.Cubic:
                                    skapi.SkCanvas.DrawLine(
                                       points[0],
                                       points[1],
                                       paint);

                                    skapi.SkCanvas.DrawLine(
                                        points[2],
                                        points[3],
                                        paint);
                                    break;
                            }
                        }

                        count++;
                    } while (pathVerb != SKPathVerb.Done);
                }
            }
        }
    }
}

