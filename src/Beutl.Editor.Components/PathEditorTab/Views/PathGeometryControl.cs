using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Beutl.Controls;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using AvaMatrix = Avalonia.Matrix;
using AvaPoint = Avalonia.Point;
using PathGeometry = Beutl.Media.PathGeometry;

namespace Beutl.Editor.Components.PathEditorTab.Views;

public class PathGeometryControl : Control
{
    public static readonly StyledProperty<Media.PathGeometry?> GeometryProperty =
        AvaloniaProperty.Register<PathGeometryControl, Media.PathGeometry?>(nameof(Geometry));

    public static readonly StyledProperty<Media.PathFigure?> FigureProperty =
        AvaloniaProperty.Register<PathGeometryControl, Media.PathFigure?>(nameof(Figure));

    public static readonly StyledProperty<Media.PathSegment?> SelectedOperationProperty =
        AvaloniaProperty.Register<PathGeometryControl, Media.PathSegment?>(nameof(SelectedOperation));

    public static readonly StyledProperty<(PathGeometry.Resource Resource, int Version)?> GeometryResourceProperty =
        AvaloniaProperty.Register<PathGeometryControl, (PathGeometry.Resource Resource, int Version)?>(
            nameof(GeometryResource));

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
        AffectsRender<PathGeometryControl>(GeometryProperty, FigureProperty, MatrixProperty, ScaleProperty,
            SelectedOperationProperty, GeometryResourceProperty);
    }

    public (PathGeometry.Resource Resource, int Version)? GeometryResource
    {
        get => GetValue(GeometryResourceProperty);
        set => SetValue(GeometryResourceProperty, value);
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
        if (change.Property == IsPlayingProperty)
        {
            IsHitTestVisible = !IsPlaying;
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Geometry != null
            && Figure != null
            && SelectedOperation != null
            && GeometryResource is { Resource: { } pathGeometry })
        {
            var figureResource = pathGeometry.Figures.FirstOrDefault(f => f.GetOriginal() == Figure);
            if (figureResource == null) return;
            int index = figureResource.Segments.FindIndex(s => s.GetOriginal() == SelectedOperation);
            if (figureResource.Segments.Count > 0 && index >= 0)
            {
                AvaMatrix mat = Matrix * AvaMatrix.CreateScale(Scale, Scale);

                bool isClosed = figureResource.IsClosed;

                void DrawLineAndShadow(AvaPoint p1, AvaPoint p2)
                {
                    context.DrawLine(s_shadowPen, p1 + new AvaPoint(1, 1), p2 + new AvaPoint(1, 1));
                    context.DrawLine(s_pen, p1, p2);
                }

                void DrawLine(Media.PathSegment.Resource op, int index, bool c1, bool c2)
                {
                    if (!isClosed && index == 0)
                    {
                        return;
                    }

                    int prevIndex = (index - 1 + figureResource.Segments.Count) % figureResource.Segments.Count;
                    AvaPoint lastPoint = default;
                    if (0 <= prevIndex && prevIndex < figureResource.Segments.Count)
                    {
                        var tmp = figureResource.Segments[prevIndex].GetEndPoint();
                        lastPoint = tmp?.ToAvaPoint() ?? default;
                    }

                    switch (op)
                    {
                        case ConicSegment.Resource conic:
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

                        case CubicBezierSegment.Resource cubic:
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

                        case Media.QuadraticBezierSegment.Resource quad:
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

                DrawLine(figureResource.Segments[index], index, false, true);
                int nextIndex = (index + 1) % figureResource.Segments.Count;

                if (0 <= nextIndex && nextIndex < figureResource.Segments.Count)
                {
                    DrawLine(figureResource.Segments[nextIndex], nextIndex, true, false);
                }
            }
        }
    }
}
