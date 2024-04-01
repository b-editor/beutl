using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

using Beutl.Media;

using AvaMatrix = Avalonia.Matrix;
using AvaPoint = Avalonia.Point;

namespace Beutl.Views;

public class PathGeometryControl : Control
{
    public static readonly StyledProperty<Media.PathGeometry?> GeometryProperty =
        AvaloniaProperty.Register<PathGeometryControl, Media.PathGeometry?>(nameof(Geometry));

    public static readonly StyledProperty<Media.PathFigure?> FigureProperty =
        AvaloniaProperty.Register<PathGeometryControl, Media.PathFigure?>(nameof(Figure));

    public static readonly StyledProperty<Media.PathSegment?> SelectedOperationProperty =
        AvaloniaProperty.Register<PathGeometryControl, Media.PathSegment?>(nameof(SelectedOperation));

    public static readonly StyledProperty<AvaMatrix> MatrixProperty =
        AvaloniaProperty.Register<PathGeometryControl, AvaMatrix>(nameof(Matrix), AvaMatrix.Identity);

    public static readonly StyledProperty<double> ScaleProperty =
        AvaloniaProperty.Register<PathGeometryControl, double>(nameof(Scale), 1.0);

    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<PathGeometryControl, bool>(nameof(IsPlaying));

    private static readonly Avalonia.Media.IPen s_pen = new Avalonia.Media.Immutable.ImmutablePen(
        Avalonia.Media.Brushes.White.ToImmutable(), 1,
        new Avalonia.Media.Immutable.ImmutableDashStyle([3, 3], 0));

    private static readonly Avalonia.Media.IPen s_shadowPen = new Avalonia.Media.Immutable.ImmutablePen(
        Avalonia.Media.Brushes.Black.ToImmutable(), 1,
        new Avalonia.Media.Immutable.ImmutableDashStyle([3, 3], 0));

    static PathGeometryControl()
    {
        AffectsRender<PathGeometryControl>(GeometryProperty, FigureProperty, MatrixProperty, ScaleProperty, SelectedOperationProperty);
    }

    public Media.PathSegment? SelectedOperation
    {
        get => GetValue(SelectedOperationProperty);
        set => SetValue(SelectedOperationProperty, value);
    }

    public AvaMatrix Matrix
    {
        get => GetValue(MatrixProperty);
        set => SetValue(MatrixProperty, value);
    }

    public Media.PathGeometry? Geometry
    {
        get => GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }

    public Media.PathFigure? Figure
    {
        get => GetValue(FigureProperty);
        set => SetValue(FigureProperty, value);
    }

    public double Scale
    {
        get => GetValue(ScaleProperty);
        set => SetValue(ScaleProperty, value);
    }

    public bool IsPlaying
    {
        get => GetValue(IsPlayingProperty);
        set => SetValue(IsPlayingProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == GeometryProperty)
        {
            if (change.OldValue is Media.PathGeometry oldValue)
            {
                oldValue.Invalidated -= OnGeometryInvalidated;
            }

            if (change.NewValue is Media.PathGeometry newValue)
            {
                newValue.Invalidated += OnGeometryInvalidated;
            }
        }
        else if (change.Property == IsPlayingProperty)
        {
            IsHitTestVisible = !IsPlaying;
        }
    }

    private void OnGeometryInvalidated(object? sender, RenderInvalidatedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsPlaying)
            {
                InvalidateVisual();
            }
        });
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Geometry != null
            && Figure != null
            && SelectedOperation != null)
        {
            int index = Figure.Segments.IndexOf(SelectedOperation);
            if (Figure.Segments.Count > 0 && index >= 0)
            {
                AvaMatrix mat = Matrix * AvaMatrix.CreateScale(Scale, Scale);

                bool isClosed = Figure.IsClosed;

                void DrawLineAndShadow(AvaPoint p1, AvaPoint p2)
                {
                    context.DrawLine(s_shadowPen, p1 + new AvaPoint(1, 1), p2 + new AvaPoint(1, 1));
                    context.DrawLine(s_pen, p1, p2);
                }

                void DrawLine(Media.PathSegment op, int index, bool c1, bool c2)
                {
                    if (!isClosed && index == 0)
                    {
                        return;
                    }

                    int prevIndex = (index - 1 + Figure.Segments.Count) % Figure.Segments.Count;
                    AvaPoint lastPoint = default;
                    if (0 <= prevIndex && prevIndex < Figure.Segments.Count
                        && Figure.Segments[prevIndex].TryGetEndPoint(out Graphics.Point tmp))
                    {
                        lastPoint = tmp.ToAvaPoint();
                    }

                    switch (op)
                    {
                        case ConicSegment conic:
                            if (c1)
                            {
                                DrawLineAndShadow(
                                    mat.Transform(lastPoint),
                                    mat.Transform(conic.ControlPoint.ToAvaPoint()));
                            }

                            if (c2)
                            {
                                DrawLineAndShadow(
                                    mat.Transform(conic.EndPoint.ToAvaPoint()),
                                    mat.Transform(conic.ControlPoint.ToAvaPoint()));
                            }
                            break;

                        case CubicBezierSegment cubic:
                            if (c1)
                            {
                                DrawLineAndShadow(
                                    mat.Transform(lastPoint),
                                    mat.Transform(cubic.ControlPoint1.ToAvaPoint()));
                            }

                            if (c2)
                            {
                                DrawLineAndShadow(
                                    mat.Transform(cubic.EndPoint.ToAvaPoint()),
                                    mat.Transform(cubic.ControlPoint2.ToAvaPoint()));
                            }
                            break;

                        case Media.QuadraticBezierSegment quad:
                            if (c1)
                            {
                                DrawLineAndShadow(
                                    mat.Transform(lastPoint),
                                    mat.Transform(quad.ControlPoint.ToAvaPoint()));
                            }

                            if (c2)
                            {
                                DrawLineAndShadow(
                                    mat.Transform(quad.EndPoint.ToAvaPoint()),
                                    mat.Transform(quad.ControlPoint.ToAvaPoint()));
                            }
                            break;
                    }
                }

                DrawLine(Figure.Segments[index], index, false, true);
                int nextIndex = (index + 1) % Figure.Segments.Count;

                if (0 <= nextIndex && nextIndex < Figure.Segments.Count)
                {
                    DrawLine(Figure.Segments[nextIndex], nextIndex, true, false);
                }
            }
        }
    }
}
