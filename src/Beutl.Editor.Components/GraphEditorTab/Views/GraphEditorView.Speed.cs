using Avalonia;
using Avalonia.Input;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.Helpers;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public partial class GraphEditorView
{
    private sealed record SpeedHandleDrag(GraphEditorKeyFrameViewModel Item, bool Incoming,
        Point Origin, Point Handle, double Velocity, double ScaleY, Point OppositeVector);
    private SpeedHandleDrag? _speedHandle;

    internal static double GetKeyVelocity(GraphEditorKeyFrameViewModel item, bool outgoing = false)
        => GraphEditorCurveMath.KeyVelocity(item, outgoing);

    internal static Point GetSpeedHandlePoint(GraphEditorKeyFrameViewModel item, bool incoming)
    {
        var channel = item.Parent;
        var model = channel.Parent;
        int index = channel.KeyFrames.IndexOf(item);
        if (index <= 0 || item.Model.Easing is not SplineEasing spline) return default;
        var previous = channel.KeyFrames[index - 1];
        double left = previous.Model.KeyTime.TimeToPixel(model.Options.Value.Scale);
        double right = item.Model.KeyTime.TimeToPixel(model.Options.Value.Scale);
        double velocity = incoming ? GetKeyVelocity(item) : GetKeyVelocity(previous, true);
        return new Point(left + (right - left) * (incoming ? spline.X2 : spline.X1) + model.Margin.Value.Left,
            model.Baseline.Value - velocity * model.ScaleY.Value);
    }

    internal void StartSpeedHandleDrag(GraphEditorKeyFrameViewModel item, bool incoming, PointerPressedEventArgs e)
    {
        if (_interactionPointer != null) return;
        Focus();
        var editor = item.Parent.Parent;
        Point handle = GetSpeedHandlePoint(item, incoming);
        var opposite = GraphEditorTangentCoupling.OppositeSegment(item, incoming);
        _speedHandle = new SpeedHandleDrag(item, incoming, e.GetPosition(grid), handle,
            (editor.Baseline.Value - handle.Y) / editor.ScaleY.Value, editor.ScaleY.Value,
            opposite != null ? GraphEditorTangentCoupling.Vector(opposite, !incoming) : default);
        CaptureInteraction(e.Pointer);
        e.Handled = true;
    }

    private void MoveSpeedHandle(PointerEventArgs e)
    {
        if (_speedHandle is not { } state || DataContext is not GraphEditorViewModel model) return;
        Point delta = e.GetPosition(grid) - state.Origin;
        if (!_editStarted && Math.Abs(delta.X) + Math.Abs(delta.Y) < 3) return;
        BeginGraphEdit(model);
        Point position = state.Handle + delta;
        double velocity = state.Velocity - (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0 : delta.Y / state.ScaleY);
        double anchorX = (state.Incoming ? state.Item.Right.Value : state.Item.Left.Value) + model.Margin.Value.Left;
        if (GraphEditorTangentCoupling.CrossedSegment(state.Item, state.Incoming, position.X - anchorX) is { } crossed)
        {
            SetVelocity(state.Item, state.Incoming, velocity, 0);
            _speedHandle = state = state with { Item = crossed, Incoming = !state.Incoming };
        }
        var channel = state.Item.Parent;
        int index = channel.KeyFrames.IndexOf(state.Item);
        if (index <= 0) return;
        var previous = channel.KeyFrames[index - 1];
        double left = previous.Model.KeyTime.TimeToPixel(model.Options.Value.Scale) + model.Margin.Value.Left;
        double width = state.Item.Model.KeyTime.TimeToPixel(model.Options.Value.Scale) + model.Margin.Value.Left - left;
        if (width <= 0) return;
        double fraction = Math.Clamp((position.X - left) / width, 0.001, 0.999);
        SetVelocity(state.Item, state.Incoming, velocity, state.Incoming ? 1 - fraction : fraction);
        if (!model.Separately.Value && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            var opposite = GraphEditorTangentCoupling.OppositeSegment(state.Item, state.Incoming);
            if (opposite != null)
            {
                Point vector = GraphEditorTangentCoupling.Opposite(model,
                    GraphEditorTangentCoupling.Vector(state.Item, state.Incoming), state.OppositeVector);
                int oppositeIndex = channel.KeyFrames.IndexOf(opposite);
                double duration = (opposite.Model.KeyTime - channel.KeyFrames[oppositeIndex - 1].Model.KeyTime).TotalSeconds;
                if (duration > 0 && Math.Abs(vector.X) > 1e-9)
                    SetVelocity(opposite, !state.Incoming, vector.Y / vector.X, Math.Abs(vector.X) / duration);
            }
        }
        UpdateSelectionAdorner();
        e.Handled = true;
    }

    private static void SetVelocity(GraphEditorKeyFrameViewModel item, bool incoming, double velocity, double? influence = null, bool replaceEasing = false)
    {
        int index = item.Parent.KeyFrames.IndexOf(item);
        if (index <= 0) return;
        var previous = item.Parent.KeyFrames[index - 1].Model;
        double difference = item.Parent.ConvertToDouble(item.Model.Value) - item.Parent.ConvertToDouble(previous.Value);
        double duration = (item.Model.KeyTime - previous.KeyTime).TotalSeconds;
        if (duration <= 0) return;
        var spline = GraphEditorCurveMath.ToSpline(item.Model.Easing);
        if (replaceEasing) spline = new SplineEasing(spline.X1, spline.Y1, spline.X2, spline.Y2);
        double fraction = Math.Clamp(influence ?? (incoming ? 1 - spline.X2 : spline.X1), 0.001, 0.999);
        double slope = difference == 0 ? 0 : velocity * duration / difference;
        double y = incoming ? 1 - slope * fraction : slope * fraction;
        if (!double.IsFinite(y) || Math.Abs(y) > float.MaxValue) return;
        if (incoming) { spline.X2 = (float)(1 - fraction); spline.Y2 = (float)y; }
        else { spline.X1 = (float)fraction; spline.Y1 = (float)y; }
        item.Model.Easing = spline;
    }

    private void ApplyVelocityDelta(GraphEditorDragSnapshot snapshot, double delta)
    {
        if (DataContext is not GraphEditorViewModel { SelectedView.Value: { } channel }) return;
        foreach (var entry in snapshot.Entries)
        {
            int index = channel.KeyFrames.IndexOf(channel.KeyFrames.FirstOrDefault(x => x.Model == entry.Model)!);
            if (index < 0) continue;
            if (index > 0) SetVelocity(channel.KeyFrames[index], true, snapshot.Velocities[entry.Model].Incoming + delta);
            if (index + 1 < channel.KeyFrames.Count) SetVelocity(channel.KeyFrames[index + 1], false, snapshot.Velocities[entry.Model].Outgoing + delta);
        }
    }
}
