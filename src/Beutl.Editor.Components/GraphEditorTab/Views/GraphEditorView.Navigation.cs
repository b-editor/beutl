using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Vector = Avalonia.Vector;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public partial class GraphEditorView
{
    private enum WheelTarget { Plot, HorizontalRuler, VerticalRuler }

    private Point? _panStart;
    private Vector _panOffset;
    private bool _fitting;

    private void OnPlotNavigationPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not GraphEditorViewModel || _interactionPointer != null) return;
        var point = e.GetCurrentPoint(scroll);
        if (!point.Properties.IsMiddleButtonPressed && !(_spacePressed && point.Properties.IsLeftButtonPressed)) return;
        StartPan(e);
    }

    private void StartPan(PointerPressedEventArgs e)
    {
        Focus();
        _panStart = e.GetPosition(scroll);
        _panOffset = scroll.Offset;
        CaptureInteraction(e.Pointer);
        graphPanel.Cursor = new Cursor(StandardCursorType.Hand);
        e.Handled = true;
    }

    private void MovePan(PointerEventArgs e)
    {
        if (_panStart is not { } start || DataContext is not GraphEditorViewModel model) return;
        Point delta = e.GetPosition(scroll) - start;
        e.Handled = true;
        SetScrollOffset(model, new Vector(_panOffset.X - delta.X,
            model.AutoZoomHeight.Value ? _panOffset.Y : _panOffset.Y - delta.Y));
    }

    private void OnContentPointerWheelChanged(object? sender, PointerWheelEventArgs e) => HandleWheel(e);
    private void OnHorizontalScalePointerWheelChanged(object? sender, PointerWheelEventArgs e) => HandleWheel(e, WheelTarget.HorizontalRuler);
    private void OnVerticalScalePointerWheelChanged(object? sender, PointerWheelEventArgs e) => HandleWheel(e, WheelTarget.VerticalRuler);

    private void HandleWheel(PointerWheelEventArgs e, WheelTarget target = WheelTarget.Plot)
    {
        if (DataContext is not GraphEditorViewModel model) return;
        e.Handled = true;
        if (_interactionPointer != null || ControlPointMoveState != null) return;
        double delta = target == WheelTarget.HorizontalRuler && e.Delta.X != 0 ? e.Delta.X
            : e.Delta.Y != 0 ? e.Delta.Y : e.Delta.X;
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (alt || e.KeyModifiers.HasFlag(KeyGestureHelper.GetCommandModifier()))
        {
            bool horizontal = target == WheelTarget.HorizontalRuler || target == WheelTarget.Plot && alt;
            if (horizontal)
                ZoomHorizontal(model, Math.Pow(1.2, delta), e.GetPosition(scroll).X);
            else if (!model.AutoZoomHeight.Value)
                ZoomVertical(model, Math.Pow(1.2, delta), e.GetPosition(scroll).Y);
        }
        else
        {
            Vector movement = target switch
            {
                WheelTarget.HorizontalRuler => new Vector(-delta * 50, 0),
                WheelTarget.VerticalRuler => new Vector(0, model.AutoZoomHeight.Value ? 0 : -delta * 50),
                _ => e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                    ? new Vector(-delta * 50, 0) : new Vector(-e.Delta.X * 50, model.AutoZoomHeight.Value ? 0 : -e.Delta.Y * 50)
            };
            SetScrollOffset(model, scroll.Offset + movement);
        }
        UpdateSelectionAdorner();
    }

    private void ZoomHorizontal(GraphEditorViewModel model, double factor, double pointerX)
    {
        _transformAnchor = null;
        float oldScale = model.Options.Value.Scale;
        float scale = (float)Math.Clamp(oldScale * factor, 0.001, 100);
        double position = (scroll.Offset.X + pointerX) / oldScale;
        model.Options.Value = model.Options.Value with { Scale = scale };
        scale = model.Options.Value.Scale;
        graphPanel.UpdateLayout();
        SetScrollOffset(model, scroll.Offset.WithX(position * scale - pointerX));
    }

    private void ZoomVertical(GraphEditorViewModel model, double factor, double pointerY)
    {
        _transformAnchor = null;
        double value = (model.Baseline.Value - scroll.Offset.Y - pointerY) / model.ScaleY.Value;
        model.ScaleY.Value = Math.Clamp(model.ScaleY.Value * factor, 0.000001, 1000000);
        model.RefreshVerticalRange();
        graphPanel.UpdateLayout();
        SetScrollOffset(model, scroll.Offset.WithY(model.Baseline.Value - value * model.ScaleY.Value - pointerY));
    }

    private void SetScrollOffset(GraphEditorViewModel model, Vector offset)
    {
        scroll.Offset = new Vector(Math.Max(0, offset.X), Math.Max(0, offset.Y));
        model.ScrollOffset.Value = scroll.Offset;
        model.Options.Value = model.Options.Value with
        {
            Offset = new Vector2((float)scroll.Offset.X, model.Options.Value.Offset.Y)
        };
    }

    private void FitGraph(bool selection, bool heightOnly = false)
    {
        if (_fitting || DataContext is not GraphEditorViewModel { SelectedView.Value: { } channel } model
            || scroll.Viewport.Width <= 0 || scroll.Viewport.Height <= 0) return;
        var keys = channel.KeyFrames.Where(x => !selection || x.IsSelected.Value).ToArray();
        if (keys.Length == 0) return;
        _fitting = true;
        try
        {
            double min = double.PositiveInfinity, max = double.NegativeInfinity;
            if (selection) channel.GetGraphRange(ref min, ref max, true);
            else foreach (var view in model.Views) view.GetGraphRange(ref min, ref max);
            if (!double.IsFinite(min) || !double.IsFinite(max)) return;
            const double padding = 32;
            double range = Math.Max(max - min, 1);
            double newScale = Math.Clamp(Math.Max(1, scroll.Viewport.Height - padding * 2) / range, 0.000001, 1000000);
            if (Math.Abs(newScale - model.ScaleY.Value) > model.ScaleY.Value * 1e-10)
                model.ScaleY.Value = newScale;
            model.RefreshVerticalRange();
            if (!heightOnly)
            {
                double first = keys.Min(x => x.Model.KeyTime.TotalSeconds);
                double last = keys.Max(x => x.Model.KeyTime.TotalSeconds);
                double duration = Math.Max(last - first, 1d / (model.Scene.FindHierarchicalParent<Project>()?.GetFrameRate() ?? 30));
                float scale = (float)Math.Clamp(Math.Max(1, scroll.Viewport.Width - padding * 2)
                    / TimeSpan.FromSeconds(duration).TimeToPixel(1), 0.001, 100);
                model.Options.Value = model.Options.Value with { Scale = scale };
            }
            graphPanel.UpdateLayout();
            double x = heightOnly ? scroll.Offset.X : keys.Min(x => x.Model.KeyTime.TimeToPixel(model.Options.Value.Scale))
                + model.Margin.Value.Left - padding;
            double y = model.Baseline.Value - ((min + max) / 2) * model.ScaleY.Value - scroll.Viewport.Height / 2;
            SetScrollOffset(model, new Vector(x, y));
            UpdateSelectionAdorner();
        }
        finally { _fitting = false; }
    }

    private void OnGraphViewportChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is GraphEditorViewModel { AutoZoomHeight.Value: true })
            Dispatcher.UIThread.Post(() => FitGraph(false, true));
    }
}
