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
        AffectsRender<PathGeometryControl>(FigureProperty, MatrixProperty, ScaleProperty,
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
        if (Figure == null
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
                if (index >= 0)
                {
                    AvaMatrix mat = Matrix * AvaMatrix.CreateScale(Scale, Scale);

                    bool isClosed = figureResource.IsClosed;

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
                        else
                        {
                            var tmp = figureResource.Segments[prevIndex].GetEndPoint();
                            lastPoint = tmp?.ToAvaPoint() ?? default;
                        }

                        (AvaPoint Incoming, AvaPoint Outgoing, AvaPoint End)? handles = op switch
                        {
                            ConicSegment.Resource conic =>
                                (conic.ControlPoint.ToAvaPoint(), conic.ControlPoint.ToAvaPoint(), conic.EndPoint.ToAvaPoint()),
                            CubicBezierSegment.Resource cubic =>
                                (cubic.ControlPoint1.ToAvaPoint(), cubic.ControlPoint2.ToAvaPoint(), cubic.EndPoint.ToAvaPoint()),
                            Media.QuadraticBezierSegment.Resource quad =>
                                (quad.ControlPoint.ToAvaPoint(), quad.ControlPoint.ToAvaPoint(), quad.EndPoint.ToAvaPoint()),
                            _ => null
                        };
                        if (handles is not { } h) return;

                        if (c1)
                            context.DrawLine(_handlePen, mat.Transform(lastPoint), mat.Transform(h.Incoming));

                        if (c2)
                            context.DrawLine(_handlePen, mat.Transform(h.End), mat.Transform(h.Outgoing));
                    }

                    DrawLine(figureResource.Segments[index], index, false, true);
                    int nextIndex = (index + 1) % figureResource.Segments.Count;

                    if (nextIndex != 0 || isClosed && figureResource.StartPoint.IsInvalid)
                    {
                        DrawLine(figureResource.Segments[nextIndex], nextIndex, true, false);
                    }
                }
            }
        });
    }
}
