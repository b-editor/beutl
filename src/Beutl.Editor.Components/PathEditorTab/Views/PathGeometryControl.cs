using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Beutl.Controls;
using Beutl.Editor.Components.Helpers;
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

    public static readonly StyledProperty<EngineResourceHandle<PathGeometry.Resource>?> GeometryResourceProperty =
        AvaloniaProperty.Register<PathGeometryControl, EngineResourceHandle<PathGeometry.Resource>?>(
            nameof(GeometryResource));

    public static readonly StyledProperty<AvaMatrix> MatrixProperty =
        AvaloniaProperty.Register<PathGeometryControl, AvaMatrix>(nameof(Matrix), AvaMatrix.Identity);

    public static readonly StyledProperty<double> ScaleProperty =
        AvaloniaProperty.Register<PathGeometryControl, double>(nameof(Scale), 1.0);

    public static readonly StyledProperty<bool> IsPlayingProperty =
        AvaloniaProperty.Register<PathGeometryControl, bool>(nameof(IsPlaying));

    private Avalonia.Media.IPen _handlePen = new Avalonia.Media.Immutable.ImmutablePen(
        new Avalonia.Media.Immutable.ImmutableSolidColorBrush(0xFF168BFF), 1);

    public PathGeometryControl()
    {
        this.GetResourceObservable("AccentFillColorDefaultBrush").Subscribe(value =>
        {
            if (value is IBrush brush)
            {
                _handlePen = new Avalonia.Media.Immutable.ImmutablePen(brush.ToImmutable(), 1);
                InvalidateVisual();
            }
        });
    }

    static PathGeometryControl()
    {
        AffectsRender<PathGeometryControl>(GeometryProperty, FigureProperty, MatrixProperty, ScaleProperty,
            SelectedOperationProperty, GeometryResourceProperty);
    }

    public EngineResourceHandle<PathGeometry.Resource>? GeometryResource
    {
        get => GetValue(GeometryResourceProperty);
        set => SetValue(GeometryResourceProperty, value);
    }

    public IReadOnlyList<Media.PathSegment> SelectedOperations { get; set; } = [];

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
        if (Geometry == null
            || Figure == null
            || (SelectedOperation == null && SelectedOperations.Count == 0)
            || GeometryResource is not { } handle)
        {
            return;
        }

        // Read holds the resource's owner off for the length of this callback, so the figure and segment
        // lists walked below cannot be replaced midway through.
        handle.Read(pathGeometry =>
        {
            var figureResource = pathGeometry.Figures.FirstOrDefault(f => f.GetOriginal() == Figure);
            if (figureResource == null) return;
            var selected = SelectedOperations.ToHashSet();
            if (SelectedOperation != null) selected.Add(SelectedOperation);
            foreach (var operation in selected)
            {
                int index = figureResource.Segments.FindIndex(s => s.GetOriginal() == operation);
                if (figureResource.Segments.Count > 0 && index >= 0)
                {
                    AvaMatrix mat = Matrix * AvaMatrix.CreateScale(Scale, Scale);

                    bool isClosed = figureResource.IsClosed;

                    void DrawHandleLine(AvaPoint p1, AvaPoint p2)
                    {
                        context.DrawLine(_handlePen, p1, p2);
                    }

                    void DrawLine(Media.PathSegment.Resource op, int index, bool c1, bool c2)
                    {
                        if (!isClosed && figureResource.StartPoint.IsInvalid && index == 0)
                        {
                            return;
                        }

                        int prevIndex = (index - 1 + figureResource.Segments.Count) % figureResource.Segments.Count;
                        AvaPoint lastPoint = default;
                        if (index == 0 && !figureResource.StartPoint.IsInvalid)
                            lastPoint = figureResource.StartPoint.ToAvaPoint();
                        else if (0 <= prevIndex && prevIndex < figureResource.Segments.Count)
                        {
                            var tmp = figureResource.Segments[prevIndex].GetEndPoint();
                            lastPoint = tmp?.ToAvaPoint() ?? default;
                        }

                        switch (op)
                        {
                            case ConicSegment.Resource conic:
                                if (c1)
                                {
                                    DrawHandleLine(
                                        mat.Transform(lastPoint),
                                        mat.Transform(conic.ControlPoint.ToAvaPoint()));
                                }

                                if (c2)
                                {
                                    DrawHandleLine(
                                        mat.Transform(conic.EndPoint.ToAvaPoint()),
                                        mat.Transform(conic.ControlPoint.ToAvaPoint()));
                                }

                                break;

                            case CubicBezierSegment.Resource cubic:
                                if (c1)
                                {
                                    DrawHandleLine(
                                        mat.Transform(lastPoint),
                                        mat.Transform(cubic.ControlPoint1.ToAvaPoint()));
                                }

                                if (c2)
                                {
                                    DrawHandleLine(
                                        mat.Transform(cubic.EndPoint.ToAvaPoint()),
                                        mat.Transform(cubic.ControlPoint2.ToAvaPoint()));
                                }

                                break;

                            case Media.QuadraticBezierSegment.Resource quad:
                                if (c1)
                                {
                                    DrawHandleLine(
                                        mat.Transform(lastPoint),
                                        mat.Transform(quad.ControlPoint.ToAvaPoint()));
                                }

                                if (c2)
                                {
                                    DrawHandleLine(
                                        mat.Transform(quad.EndPoint.ToAvaPoint()),
                                        mat.Transform(quad.ControlPoint.ToAvaPoint()));
                                }

                                break;
                        }
                    }

                    DrawLine(figureResource.Segments[index], index, false, true);
                    int nextIndex = (index + 1) % figureResource.Segments.Count;

                    if (0 <= nextIndex && nextIndex < figureResource.Segments.Count
                        && (nextIndex != 0 || isClosed && figureResource.StartPoint.IsInvalid))
                    {
                        DrawLine(figureResource.Segments[nextIndex], nextIndex, true, false);
                    }
                }
            }
        });
    }
}
