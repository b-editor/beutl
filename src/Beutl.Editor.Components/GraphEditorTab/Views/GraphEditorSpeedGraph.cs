using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.Helpers;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public sealed class GraphEditorSpeedGraph : Control
{
    private readonly ConditionalWeakTable<GraphEditorKeyFrameViewModel, CachedCurve> _curves = new();

    private readonly record struct CurveState(Easing Easing, double Left, double Right, double Duration,
        double Difference, double Baseline, double ScaleY, float X1, float Y1, float X2, float Y2);

    private sealed class CachedCurve
    {
        public CurveState State;
        public StreamGeometry? Geometry;
    }

    internal StreamGeometry GetCurveGeometry(GraphEditorKeyFrameViewModel previous, GraphEditorKeyFrameViewModel item)
    {
        var model = item.Parent.Parent;
        var easing = item.Model.Easing;
        var spline = easing as SplineEasing;
        var state = new CurveState(easing,
            previous.Model.KeyTime.TimeToPixel(model.Options.Value.Scale) + model.Margin.Value.Left,
            item.Model.KeyTime.TimeToPixel(model.Options.Value.Scale) + model.Margin.Value.Left,
            (item.Model.KeyTime - previous.Model.KeyTime).TotalSeconds,
            item.Parent.ConvertToDouble(item.Model.Value) - item.Parent.ConvertToDouble(previous.Model.Value),
            model.Baseline.Value, model.ScaleY.Value,
            spline?.X1 ?? 0, spline?.Y1 ?? 0, spline?.X2 ?? 0, spline?.Y2 ?? 0);
        var cached = _curves.GetOrCreateValue(item);
        // External easing implementations may have mutable state without change notifications.
        if (cached.Geometry != null && cached.State == state && easing.GetType().Assembly == typeof(Easing).Assembly)
            return cached.Geometry;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            int steps = (int)Math.Clamp(state.Right - state.Left, 32, 512);
            for (int n = 0; n <= steps; n++)
            {
                double progress = n / (double)steps;
                double velocity = GraphEditorCurveMath.Velocity(easing, progress, state.Difference, state.Duration);
                var point = new Point(state.Left + (state.Right - state.Left) * progress, state.Baseline - velocity * state.ScaleY);
                if (n == 0) path.BeginFigure(point, false);
                else path.LineTo(point);
            }
            path.EndFigure(false);
        }
        cached.State = state;
        cached.Geometry = geometry;
        return geometry;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        _curves.Clear();
        base.OnDataContextChanged(e);
        InvalidateVisual();
    }

    public GraphEditorSpeedGraph() => ActualThemeVariantChanged += (_, _) => InvalidateVisual();

    private GraphEditorView? Owner => this.FindAncestorOfType<GraphEditorView>();

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (DataContext is not GraphEditorViewModel model || Owner is not { } owner) return;
        var selectedBrush = this.TryFindResource("GraphEditorKeyFrameBrush", ActualThemeVariant, out var resource)
            ? resource as IBrush ?? Brushes.Orange : Brushes.Orange;
        foreach (var channel in model.Views)
        {
            IBrush brush = channel.Stroke.Value ?? Brushes.White;
            var pen = new Pen(brush, 2);
            using var opacity = context.PushOpacity(channel.IsSelected.Value ? 1 : 0.4);
            for (int i = 1; i < channel.KeyFrames.Count; i++)
            {
                var item = channel.KeyFrames[i];
                var previous = channel.KeyFrames[i - 1];
                if (item.Model.KeyTime <= previous.Model.KeyTime) continue;
                context.DrawGeometry(null, pen, GetCurveGeometry(previous, item));
                if (channel.IsSelected.Value && item.Model.Easing is SplineEasing)
                {
                    if (previous.IsSelected.Value) DrawHandle(item, false);
                    if (item.IsSelected.Value) DrawHandle(item, true);
                }
            }
            if (channel.IsSelected.Value)
                foreach (var item in channel.KeyFrames)
                {
                    var point = owner.GetKeyPoint(item);
                    context.DrawRectangle(item.IsSelected.Value ? selectedBrush : brush, null,
                        new Rect(point - new Point(4, 4), new Size(8, 8)));
                }
        }
        void DrawHandle(GraphEditorKeyFrameViewModel item, bool incoming)
        {
            Point handle = GraphEditorView.GetSpeedHandlePoint(item, incoming);
            double x = incoming ? item.Model.KeyTime.TimeToPixel(model.Options.Value.Scale)
                : item.Parent.KeyFrames[item.Parent.KeyFrames.IndexOf(item) - 1].Model.KeyTime.TimeToPixel(model.Options.Value.Scale);
            context.DrawLine(new Pen(selectedBrush, 1), new Point(x + model.Margin.Value.Left, handle.Y), handle);
            context.DrawEllipse(selectedBrush, null, handle, 3.5, 3.5);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || DataContext is not GraphEditorViewModel { SelectedView.Value: { } channel } || Owner is not { } owner) return;
        Point position = e.GetPosition(this);
        GraphEditorKeyFrameViewModel? key = channel.KeyFrames.FirstOrDefault(x => Near(position, owner.GetKeyPoint(x), 7));
        if (key != null && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            owner.StartKeyFrameDrag(key, e);
            return;
        }
        for (int i = 1; i < channel.KeyFrames.Count; i++)
        {
            var item = channel.KeyFrames[i];
            if (item.Model.Easing is not SplineEasing) continue;
            if (item.IsSelected.Value && Near(position, GraphEditorView.GetSpeedHandlePoint(item, true), 7))
            {
                owner.StartSpeedHandleDrag(item, true, e); return;
            }
            if (channel.KeyFrames[i - 1].IsSelected.Value && Near(position, GraphEditorView.GetSpeedHandlePoint(item, false), 7))
            {
                owner.StartSpeedHandleDrag(item, false, e); return;
            }
        }
        if (key != null) owner.StartKeyFrameDrag(key, e);
    }

    private static bool Near(Point first, Point second, double tolerance) =>
        Math.Abs(first.X - second.X) <= tolerance && Math.Abs(first.Y - second.Y) <= tolerance;
}
