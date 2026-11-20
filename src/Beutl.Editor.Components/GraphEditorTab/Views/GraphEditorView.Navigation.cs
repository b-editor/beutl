using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Services;
using Microsoft.Extensions.DependencyInjection;
using Vector = Avalonia.Vector;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public partial class GraphEditorView
{
    private enum WheelTarget { Plot, HorizontalRuler, VerticalRuler }

    private Point? _panStart;
    private Vector _panOffset;
    private bool _fitting;
    private (WheelTarget Target, float ScaleX, double ScaleY, double Baseline, Vector Offset)? _touchGesture;

    private void OnPlotNavigationPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not GraphEditorViewModel || _interactionPointer != null) return;
        var point = e.GetCurrentPoint(scroll);
        if (!point.Properties.IsMiddleButtonPressed && !(_space.IsPressed && point.Properties.IsLeftButtonPressed)) return;
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
        KeyModifiers modifiers = _getWheelModifiers(e);
        if (modifiers.HasFlag(KeyModifiers.Alt) || modifiers.HasFlag(KeyGestureHelper.GetCommandModifier()))
        {
            ZoomAtPointer(model, target, modifiers, Math.Pow(1.2, delta), e.GetPosition(scroll));
        }
        else
        {
            Vector plotDelta = e.Delta;
            bool gestureAxes = target == WheelTarget.Plot && _usesGestureAxes(e);
            if (!gestureAxes && OperatingSystem.IsWindows() && e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                plotDelta = new Vector(plotDelta.Y, plotDelta.X);
            if (!gestureAxes && !model.EditorContext.GetRequiredService<GlobalConfiguration>().EditorConfig.SwapTimelineScrollDirection.Value)
                plotDelta = new Vector(plotDelta.Y, plotDelta.X);
            Vector movement = target switch
            {
                WheelTarget.HorizontalRuler => new Vector(-delta * 50, 0),
                WheelTarget.VerticalRuler => new Vector(0, model.AutoZoomHeight.Value ? 0 : -delta * 50),
                _ => new Vector(-plotDelta.X * 50, model.AutoZoomHeight.Value ? 0 : -plotDelta.Y * 50)
            };
            SetScrollOffset(model, scroll.Offset + movement);
        }
        UpdateSelectionAdorner();
    }

    private void OnPlotMagnify(object? sender, PointerDeltaEventArgs e) => HandleMagnify(e, WheelTarget.Plot);
    private void OnHorizontalRulerMagnify(object? sender, PointerDeltaEventArgs e) => HandleMagnify(e, WheelTarget.HorizontalRuler);
    private void OnVerticalRulerMagnify(object? sender, PointerDeltaEventArgs e) => HandleMagnify(e, WheelTarget.VerticalRuler);

    private void HandleMagnify(PointerDeltaEventArgs e, WheelTarget target)
    {
        if (DataContext is not GraphEditorViewModel model) return;
        e.Handled = true;
        if (_interactionPointer != null || ControlPointMoveState != null) return;

        // Native magnification already reports the relative scale change.
        double factor = 1 + e.Delta.X;
        if (!double.IsFinite(factor) || factor <= 0) return;

        ZoomAtPointer(model, target, e.KeyModifiers, factor, e.GetPosition(scroll));
        UpdateSelectionAdorner();
    }

    private void ZoomAtPointer(GraphEditorViewModel model, WheelTarget target, KeyModifiers modifiers, double factor, Point position)
    {
        bool horizontal = target == WheelTarget.HorizontalRuler
            || target == WheelTarget.Plot && (modifiers.HasFlag(KeyModifiers.Alt) || !modifiers.HasFlag(KeyModifiers.Shift));
        if (horizontal)
            ZoomHorizontal(model, factor, position.X);
        else if (!model.AutoZoomHeight.Value)
            ZoomVertical(model, factor, position.Y);
    }

    private void OnGraphTouchStarted(WheelTarget target)
    {
        _mouseFlag = TimelineHelper.MouseFlags.Free;
        _rulerDrag.Release();
        _panStart = null;
        if (DataContext is not GraphEditorViewModel model || _interactionPointer != null || ControlPointMoveState != null)
        {
            _touchGesture = null;
            return;
        }

        _touchGesture = (target, model.Options.Value.Scale, model.ScaleY.Value, model.Baseline.Value, scroll.Offset);
    }

    private void OnGraphTouchUpdated(TwoFingerTouchState state)
    {
        if (DataContext is not GraphEditorViewModel model
            || _touchGesture is not { } start
            || _interactionPointer != null
            || ControlPointMoveState != null)
        {
            return;
        }

        _transformAnchor = null;
        bool zoomsVertical = start.Target == WheelTarget.VerticalRuler
            || (start.Target == WheelTarget.Plot && state.KeyModifiers.HasFlag(KeyModifiers.Shift) && !state.KeyModifiers.HasFlag(KeyModifiers.Alt));

        double newOffsetX = start.Offset.X;
        double newOffsetY = start.Offset.Y;

        if (zoomsVertical)
        {
            if (!model.AutoZoomHeight.Value)
            {
                double anchorValue = (start.Baseline - start.Offset.Y - state.StartCenter.Y) / start.ScaleY;
                model.ScaleY.Value = Math.Clamp(start.ScaleY * state.ScaleFactor, 0.000001, 1000000);
                model.RefreshVerticalRange();
                graphPanel.UpdateLayout();
                newOffsetY = model.Baseline.Value - anchorValue * model.ScaleY.Value - state.CurrentCenter.Y;
            }

            if (start.Target == WheelTarget.Plot)
            {
                newOffsetX = start.Offset.X - state.PanDelta.X;
            }
        }
        else
        {
            float scale = (float)Math.Clamp(start.ScaleX * state.ScaleFactor, 0.001, 100);
            double anchorTimePixel = (start.Offset.X + state.StartCenter.X) / start.ScaleX;
            model.Options.Value = model.Options.Value with { Scale = scale };
            scale = model.Options.Value.Scale;
            graphPanel.UpdateLayout();
            newOffsetX = anchorTimePixel * scale - state.CurrentCenter.X;
            if (start.Target == WheelTarget.Plot && !model.AutoZoomHeight.Value)
            {
                newOffsetY = start.Offset.Y - state.PanDelta.Y;
            }
        }

        SetScrollOffset(model, new Vector(newOffsetX, newOffsetY));
        UpdateSelectionAdorner();
    }

    private void OnGraphTouchEnded() => _touchGesture = null;

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
                double first = keys.Min(x => x.Model.KeyTime.Value.ToSeconds());
                double last = keys.Max(x => x.Model.KeyTime.Value.ToSeconds());
                double duration = Math.Max(last - first, 1d / SceneTimeRangeService.GetFrameRate(model.Scene).ToDouble());
                float scale = (float)Math.Clamp(Math.Max(1, scroll.Viewport.Width - padding * 2)
                    / MediaTime.FromSeconds(duration).TimeToPixel(1), 0.001, 100);
                model.Options.Value = model.Options.Value with { Scale = scale };
            }
            graphPanel.UpdateLayout();
            double x = heightOnly ? scroll.Offset.X : keys.Min(x => x.Model.KeyTime.Value.TimeToPixel(model.Options.Value.Scale))
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
