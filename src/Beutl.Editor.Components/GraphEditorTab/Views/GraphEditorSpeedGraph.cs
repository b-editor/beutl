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
            using var opacity = context.PushOpacity(channel.IsSelected.Value ? 1 : 0.4);
            for (int i = 1; i < channel.KeyFrames.Count; i++)
            {
                var item = channel.KeyFrames[i];
                var previous = channel.KeyFrames[i - 1];
                double left = previous.Model.KeyTime.TimeToPixel(model.Options.Value.Scale) + model.Margin.Value.Left;
                double right = item.Model.KeyTime.TimeToPixel(model.Options.Value.Scale) + model.Margin.Value.Left;
                double duration = (item.Model.KeyTime - previous.Model.KeyTime).TotalSeconds;
                if (duration <= 0) continue;
                double difference = channel.ConvertToDouble(item.Model.Value) - channel.ConvertToDouble(previous.Model.Value);
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    int steps = (int)Math.Clamp(right - left, 32, 512);
                    for (int n = 0; n <= steps; n++)
                    {
                        double progress = n / (double)steps;
                        double velocity = GraphEditorCurveMath.Velocity(item.Model.Easing, progress, difference, duration);
                        var point = new Point(left + (right - left) * progress, model.Baseline.Value - velocity * model.ScaleY.Value);
                        if (n == 0) path.BeginFigure(point, false);
                        else path.LineTo(point);
                    }
                    path.EndFigure(false);
                }
                context.DrawGeometry(null, new Pen(brush, 2), geometry);
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
