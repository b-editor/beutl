using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Editor.Components.GraphEditorTab.ViewModels;
using Beutl.Editor.Components.Helpers;
using Beutl.Language;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.GraphEditorTab.Views;

public partial class GraphEditorView
{
    private IPointer? _interactionPointer;
    private Point? _marqueeStart;
    private Point? _seekClickStart;
    private IKeyFrame[] _marqueeSelection = [];
    private bool _spacePressed;
    private bool _editStarted;
    private bool _finishingInteraction;
    private Rect? _selectionBounds;
    private Point? _transformAnchor;

    private void AttachGraphInteractions(GraphEditorViewModel model)
    {
        model.IsSpeedGraph.Subscribe(speed => GraphTypePicker.SelectedIndex = speed ? 1 : 0).DisposeWith(_disposables);
        foreach (var channel in model.Views)
        {
            EventHandler selectionChanged = (_, _) => { _transformAnchor = null; UpdateSelectionAdorner(); };
            channel.SelectionChanged += selectionChanged;
            EventHandler rangeChanged = (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (model.AutoZoomHeight.Value && !model.IsEditing) FitGraph(false, true);
                UpdateSelectionAdorner();
            });
            channel.VerticalRangeChanged += rangeChanged;
            Disposable.Create(() => channel.VerticalRangeChanged -= rangeChanged).DisposeWith(_disposables);
            Disposable.Create(() => channel.SelectionChanged -= selectionChanged).DisposeWith(_disposables);
        }
        model.SelectedView.Subscribe(_ => { _transformAnchor = null; UpdateSelectionAdorner(); })
            .DisposeWith(_disposables);
        model.IsSpeedGraph.Skip(1).Subscribe(_ =>
        {
            model.RefreshVerticalRange();
            Dispatcher.UIThread.Post(() => { FitGraph(false, true); UpdateSelectionAdorner(); });
        }).DisposeWith(_disposables);
        model.ShowTransformBox.Subscribe(_ => UpdateSelectionAdorner()).DisposeWith(_disposables);
        model.Baseline.Subscribe(_ => UpdateSelectionAdorner()).DisposeWith(_disposables);
        model.AutoZoomHeight.Subscribe(enabled => { if (enabled) FitGraph(false, true); })
            .DisposeWith(_disposables);
    }

    internal void StartKeyFrameDrag(GraphEditorKeyFrameViewModel item, PointerPressedEventArgs e)
    {
        if (DataContext is not GraphEditorViewModel model || _interactionPointer != null) return;
        Focus();
        var channel = item.Parent;
        bool selected = item.IsSelected.Value;
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!selected)
            channel.SetSelection(shift
                ? channel.KeyFrames.Where(x => x.IsSelected.Value).Select(x => x.Model).Append(item.Model)
                : [item.Model]);
        KeyTimeMoveState = new KeyTimeMoveState
        {
            KeyFrame = item.Model,
            KeyFrameViewModel = item,
            Origin = e.GetPosition(grid),
            DragStart = e.GetPosition(grid),
            Snapshot = new GraphEditorDragSnapshot(channel),
            ToggleOnRelease = shift && selected
        };
        CaptureInteraction(e.Pointer);
        e.Handled = true;
    }

    private void CaptureInteraction(IPointer pointer)
    {
        _interactionPointer = pointer;
        pointer.Capture(graphPanel);
    }

    private void BeginGraphEdit(GraphEditorViewModel model)
    {
        if (_editStarted) return;
        model.HistoryManager.FlushPendingMutations();
        model.HistoryManager.Commit();
        model.BeginEditing();
        _editStarted = true;
    }

    private void OnPlotPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Handled || DataContext is not GraphEditorViewModel { SelectedView.Value: { } channel } model
            || !e.GetCurrentPoint(graphPanel).Properties.IsLeftButtonPressed || _interactionPointer != null) return;
        Focus();
        Point position = e.GetPosition(graphPanel);
        if (model.ShowTransformBox.Value && _selectionBounds is { } bounds)
        {
            int handle = SelectionAdorner.HitHandle(position);
            if (handle >= 0)
            {
                var first = channel.KeyFrames.First(x => x.IsSelected.Value);
                KeyTimeMoveState = new KeyTimeMoveState
                {
                    KeyFrame = first.Model,
                    KeyFrameViewModel = first,
                    Origin = e.GetPosition(grid),
                    DragStart = e.GetPosition(grid),
                    Snapshot = new GraphEditorDragSnapshot(channel),
                    TransformHandle = handle,
                    TransformBounds = bounds,
                    TransformAnchor = _transformAnchor ?? bounds.Center
                };
                CaptureInteraction(e.Pointer);
                e.Handled = true;
                return;
            }
        }
        if (!e.KeyModifiers.HasFlag(KeyGestureHelper.GetCommandModifier()))
        {
            _mouseFlag = TimelineHelper.MouseFlags.SeekBarPressed;
            _seekClickStart = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? null : e.GetPosition(grid);
            CaptureInteraction(e.Pointer);
            OnContentPointerMoved(graphPanel, e);
            return;
        }

        _marqueeStart = position;
        _marqueeSelection = e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            ? channel.KeyFrames.Where(x => x.IsSelected.Value).Select(x => x.Model).ToArray() : [];
        channel.SetSelection(_marqueeSelection);
        CaptureInteraction(e.Pointer);
        e.Handled = true;
    }

    private void OnGraphPanelPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not GraphEditorViewModel { SelectedView.Value: { } channel } model) return;
        if (_panStart != null) { MovePan(e); return; }
        if (_speedHandle != null) { MoveSpeedHandle(e); return; }
        if (_marqueeStart is { } start)
        {
            var rectangle = new Rect(start, e.GetPosition(graphPanel)).Normalize();
            SelectionAdorner.Marquee = rectangle;
            channel.SetSelection(channel.KeyFrames.Where(x => rectangle.Contains(GetKeyPoint(x)))
                .Select(x => x.Model).Concat(_marqueeSelection));
            SelectionAdorner.InvalidateVisual();
            e.Handled = true;
            return;
        }
        if (KeyTimeMoveState is not { Snapshot: { } snapshot } state) return;
        Point delta = e.GetPosition(grid) - state.Origin + state.ScrollDelta;
        if (!state.HasMoved && Math.Abs(delta.X) + Math.Abs(delta.Y) < 3) return;
        state.HasMoved = true;
        state.ToggleOnRelease = false;
        if (state.TransformHandle == 8)
        {
            _transformAnchor = state.TransformAnchor + delta;
            UpdateSelectionAdorner();
            e.Handled = true;
            return;
        }
        BeginGraphEdit(model);
        if (state.TransformHandle >= 0)
        {
            TransformSelection(state, delta, e.KeyModifiers);
        }
        else
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                state.HorizontalConstraint ??= Math.Abs(delta.X) >= Math.Abs(delta.Y);
                delta = state.HorizontalConstraint.Value ? delta.WithY(0) : delta.WithX(0);
            }
            else state.HorizontalConstraint = null;
            double seconds = delta.X.PixelToTimeSpan(model.Options.Value.Scale).TotalSeconds;
            double value = -delta.Y / model.ScaleY.Value;
            if (model.Snap.Value && !e.KeyModifiers.HasFlag(KeyModifiers.Alt))
            {
                if (delta.X != 0) seconds = SnapTime(snapshot, seconds);
                if (delta.Y != 0 && !model.IsSpeedGraph.Value) value = SnapValue(snapshot, value);
            }
            if (model.IsSpeedGraph.Value)
            {
                snapshot.Apply(entry => (entry.Time.TotalSeconds + seconds, entry.Number));
                ApplyVelocityDelta(snapshot, value);
            }
            else snapshot.Apply(entry => (entry.Time.TotalSeconds + seconds, entry.Number + value));
        }
        state.AppliedDelta = delta;
        var pointer = e.GetPosition(scroll);
        double dx = pointer.X < 0 ? pointer.X : Math.Max(0, pointer.X - scroll.Viewport.Width);
        double dy = pointer.Y < 0 ? pointer.Y : Math.Max(0, pointer.Y - scroll.Viewport.Height);
        if (dx != 0 || dy != 0)
        {
            var previousOffset = scroll.Offset;
            SetScrollOffset(model, scroll.Offset + new Avalonia.Vector(Math.Clamp(dx, -24, 24),
                model.AutoZoomHeight.Value ? 0 : Math.Clamp(dy, -24, 24)));
            state.ScrollDelta += (Point)(scroll.Offset - previousOffset);
        }
        UpdateSelectionAdorner();
        e.Handled = true;
    }

    private double SnapTime(GraphEditorDragSnapshot snapshot, double delta)
    {
        if (DataContext is not GraphEditorViewModel model) return delta;
        var selected = snapshot.Entries.Select(x => x.Model).ToHashSet();
        double offset = model.UseGlobalClock.Value ? 0 : model.Element?.Start.TotalSeconds ?? 0;
        var targets = model.Animation.KeyFrames.Where(x => !selected.Contains(x)).Select(x => x.KeyTime.TotalSeconds)
            .Concat([model.CurrentTime.Value.TotalSeconds - offset, model.Scene.Start.TotalSeconds - offset,
                (model.Scene.Start + model.Scene.Duration).TotalSeconds - offset, 0]);
        double tolerance = 6d.PixelToTimeSpan(model.Options.Value.Scale).TotalSeconds;
        double best = tolerance;
        double correction = 0;
        foreach (double target in targets)
            foreach (var entry in snapshot.Entries)
            {
                double distance = target - (entry.Time.TotalSeconds + delta);
                if (Math.Abs(distance) < best) { best = Math.Abs(distance); correction = distance; }
            }
        return delta + correction;
    }

    private double SnapValue(GraphEditorDragSnapshot snapshot, double delta)
    {
        if (DataContext is not GraphEditorViewModel { SelectedView.Value: { } channel } model) return delta;
        var selected = snapshot.Entries.Select(x => x.Model).ToHashSet();
        double best = 6 / model.ScaleY.Value;
        double correction = 0;
        foreach (double target in channel.KeyFrames.Where(x => !selected.Contains(x.Model))
                     .Select(x => channel.ConvertToDouble(x.Model.Value)).Append(0))
            foreach (var entry in snapshot.Entries)
            {
                double distance = target - entry.Number - delta;
                if (Math.Abs(distance) < best) { best = Math.Abs(distance); correction = distance; }
            }
        return delta + correction;
    }

    private void TransformSelection(KeyTimeMoveState state, Point delta, KeyModifiers modifiers)
    {
        if (DataContext is not GraphEditorViewModel model || state.Snapshot is not { } snapshot) return;
        Rect bounds = state.TransformBounds;
        Point handle = GraphEditorSelectionAdorner.HandlePoint(bounds, state.TransformHandle);
        Point anchor = modifiers.HasFlag(KeyGestureHelper.GetCommandModifier()) ? state.TransformAnchor
            : bounds.Center * 2 - handle;
        bool horizontal = state.TransformHandle is not (1 or 5);
        bool vertical = !model.IsSpeedGraph.Value && state.TransformHandle is not (3 or 7);
        double sx = horizontal && bounds.Width > 0.01 ? (handle.X + delta.X - anchor.X) / (handle.X - anchor.X) : 1;
        double sy = vertical && bounds.Height > 0.01 ? (handle.Y + delta.Y - anchor.Y) / (handle.Y - anchor.Y) : 1;
        if (modifiers.HasFlag(KeyModifiers.Shift) && horizontal && vertical)
            sx = sy = Math.Abs(sx - 1) > Math.Abs(sy - 1) ? sx : sy;
        double originTime = (anchor.X - model.Margin.Value.Left).PixelToTimeSpan(model.Options.Value.Scale).TotalSeconds;
        double originValue = (snapshot.InitialBaseline - anchor.Y) / model.ScaleY.Value;
        snapshot.Apply(entry => (originTime + (entry.Time.TotalSeconds - originTime) * sx,
            originValue + (entry.Number - originValue) * sy), sx, sy, transformHandles: true);
    }

    private void OnGraphPanelPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is not GraphEditorViewModel model || _interactionPointer == null) return;
        if (e.InitialPressMouseButton != MouseButton.Left && e.InitialPressMouseButton != MouseButton.Middle) return;
        if (_mouseFlag == TimelineHelper.MouseFlags.SeekBarPressed)
        {
            OnContentPointerMoved(graphPanel, e);
            if (_seekClickStart != null)
                model.SelectedView.Value?.SetSelection([]);
        }
        if (KeyTimeMoveState is { ToggleOnRelease: true, HasMoved: false } state
            && model.SelectedView.Value is { } channel)
            channel.SetSelection(channel.KeyFrames.Where(x => x.IsSelected.Value && x.Model != state.KeyFrame).Select(x => x.Model));
        FinishInteraction(model, false);
        e.Handled = true;
    }

    private void OnPlotPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_finishingInteraction && _interactionPointer == e.Pointer && DataContext is GraphEditorViewModel model)
            FinishInteraction(model, true);
    }

    private void FinishInteraction(GraphEditorViewModel model, bool cancel)
    {
        if (_finishingInteraction) return;
        _finishingInteraction = true;
        try
        {
            ControlPointMoveState?.Complete?.Invoke(cancel);
            if (_editStarted)
            {
                if (cancel) model.HistoryManager.Rollback();
                model.EndEditting();
            }
            _editStarted = false;
            KeyTimeMoveState = null;
            _speedHandle = null;
            _marqueeStart = null;
            _panStart = null;
            _seekClickStart = null;
            if (_mouseFlag == TimelineHelper.MouseFlags.SeekBarPressed)
                _mouseFlag = TimelineHelper.MouseFlags.Free;
            SelectionAdorner.Marquee = null;
            var pointer = _interactionPointer;
            _interactionPointer = null;
            pointer?.Capture(null);
            graphPanel.Cursor = Cursor.Default;
            UpdateSelectionAdorner();
        }
        finally { _finishingInteraction = false; }
    }

    internal Point GetKeyPoint(GraphEditorKeyFrameViewModel item)
    {
        var model = item.Parent.Parent;
        double value = model.IsSpeedGraph.Value ? GetKeyVelocity(item) : item.Parent.ConvertToDouble(item.Model.Value);
        return new Point(item.Model.KeyTime.TimeToPixel(model.Options.Value.Scale) + model.Margin.Value.Left,
            model.Baseline.Value - value * model.ScaleY.Value);
    }

    private void UpdateSelectionAdorner()
    {
        if (DataContext is not GraphEditorViewModel { SelectedView.Value: { } channel } model) return;
        var points = channel.KeyFrames.Where(x => x.IsSelected.Value).Select(GetKeyPoint).ToArray();
        _selectionBounds = points.Length > 1
            ? new Rect(new Point(points.Min(x => x.X), points.Min(x => x.Y)),
                new Point(points.Max(x => x.X), points.Max(x => x.Y))) : null;
        SelectionAdorner.Selection = model.ShowTransformBox.Value ? _selectionBounds : null;
        SelectionAdorner.Anchor = _transformAnchor;
        SelectionAdorner.InvalidateVisual();
        SpeedGraph.InvalidateVisual();
    }

    private async void OnGraphKeyDown(object? sender, KeyEventArgs e)
    {
        if (VelocityFlyout?.IsOpen == true) return;
        if (e.Source is TextBox || DataContext is not GraphEditorViewModel { SelectedView.Value: { } channel } model) return;
        var command = KeyGestureHelper.GetCommandModifier();
        if (e.Key == Key.Space) { _spacePressed = true; e.Handled = true; return; }
        if (e.Key == Key.Escape)
        {
            if (ControlPointMoveState is { } controlPoint) controlPoint.Complete?.Invoke(true);
            else if (_interactionPointer != null) FinishInteraction(model, true);
            else channel.SetSelection([]);
        }
        else if (_interactionPointer != null || ControlPointMoveState != null) return;
        else if (e.Key == Key.A && e.KeyModifiers.HasFlag(command))
            channel.SetSelection(e.KeyModifiers.HasFlag(KeyModifiers.Shift)
                ? [] : channel.KeyFrames.Select(x => x.Model));
        else if (e.Key == Key.F2 && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) channel.SetSelection([]);
        else if (e.Key is Key.C or Key.X && e.KeyModifiers.HasFlag(command))
        {
            e.Handled = true;
            var copied = channel.KeyFrames.Where(x => x.IsSelected.Value).Select(x => x.Model).ToArray();
            if (await model.CopySelectionAsync(TopLevel.GetTopLevel(this)?.Clipboard) && e.Key == Key.X)
                model.DeleteKeyFrames(copied);
        }
        else if (e.Key == Key.V && e.KeyModifiers.HasFlag(command))
        {
            e.Handled = true;
            await model.PasteSelectionAsync(TopLevel.GetTopLevel(this)?.Clipboard);
        }
        else if (e.Key is Key.Delete or Key.Back) DeleteSelection();
        else if (e.Key == Key.K && e.KeyModifiers.HasFlag(command | KeyModifiers.Shift))
        {
            e.Handled = true;
            ShowVelocityFlyout();
        }
        else if (e.Key == Key.F9) ApplyEasing(e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "EaseIn"
            : e.KeyModifiers.HasFlag(command) ? "EaseOut" : "Ease");
        else if (e.Key is Key.Left or Key.Right && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            double frames = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            double delta = (e.Key == Key.Left ? -frames : frames) / (model.Scene.FindHierarchicalParent<Project>()?.GetFrameRate() ?? 30);
            var snapshot = new GraphEditorDragSnapshot(channel);
            if (snapshot.Entries.Length > 0)
                model.HistoryManager.ExecuteInTransaction(() => snapshot.Apply(entry => (entry.Time.TotalSeconds + delta, entry.Number)), CommandNames.MoveKeyFrame);
        }
        else if (e.Key is Key.J or Key.K)
        {
            var times = channel.KeyFrames.Select(x => x.Model.KeyTime + (model.UseGlobalClock.Value ? TimeSpan.Zero : model.Element?.Start ?? TimeSpan.Zero));
            var target = e.Key == Key.J ? times.Where(x => x < model.CurrentTime.Value).OrderDescending().Select(x => (TimeSpan?)x).FirstOrDefault()
                : times.Where(x => x > model.CurrentTime.Value).Order().Select(x => (TimeSpan?)x).FirstOrDefault();
            if (target.HasValue) model.CurrentTime.Value = target.Value;
        }
        else if (e.Key == Key.F) FitGraph(e.KeyModifiers.HasFlag(KeyModifiers.Shift));
        else if (e.Key == Key.Z && e.KeyModifiers.HasFlag(command))
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) model.HistoryManager.Redo();
            else model.HistoryManager.Undo();
        }
        else return;
        UpdateSelectionAdorner();
        e.Handled = true;
    }

    private void OnGraphKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space) { _spacePressed = false; e.Handled = true; }
    }

    private void DeleteSelection()
    {
        if (DataContext is not GraphEditorViewModel { SelectedView.Value: { } channel } model) return;
        var selected = channel.KeyFrames.Where(x => x.IsSelected.Value).Select(x => x.Model).ToArray();
        if (selected.Length == 0) return;
        model.DeleteKeyFrames(selected);
        channel.SetSelection([]);
    }

    private void ApplyEasing(string mode)
    {
        if (DataContext is not GraphEditorViewModel { SelectedView.Value: { } channel } model) return;
        if (!channel.KeyFrames.Any(x => x.IsSelected.Value)) return;
        model.HistoryManager.ExecuteInTransaction(() =>
        {
            for (int i = 1; i < channel.KeyFrames.Count; i++)
            {
                var item = channel.KeyFrames[i];
                if (mode == "Hold")
                {
                    if (channel.KeyFrames[i - 1].IsSelected.Value) item.Model.Easing = new HoldEasing();
                    continue;
                }
                bool incoming = item.IsSelected.Value;
                bool outgoing = channel.KeyFrames[i - 1].IsSelected.Value;
                if (!incoming && !outgoing) continue;
                var spline = GraphEditorCurveMath.ToSpline(item.Model.Easing);
                item.Model.Easing = spline;
                if (outgoing) { spline.X1 = 1f / 3; spline.Y1 = mode is "Ease" or "EaseOut" ? 0 : 1f / 3; }
                if (incoming) { spline.X2 = 2f / 3; spline.Y2 = mode is "Ease" or "EaseIn" ? 1 : 2f / 3; }
            }
        }, CommandNames.EditKeyFrame);
        model.RefreshVerticalRange();
        UpdateSelectionAdorner();
    }

    private void GraphTypeSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is GraphEditorViewModel model && GraphTypePicker.SelectedIndex is 0 or 1)
            model.IsSpeedGraph.Value = GraphTypePicker.SelectedIndex == 1;
    }

    private void GraphActionClick(object? sender, RoutedEventArgs e)
    {
        string? action = (sender as Control)?.Tag as string;
        if (action is "ValueGraph" or "SpeedGraph" && DataContext is GraphEditorViewModel graph)
            graph.IsSpeedGraph.Value = action == "SpeedGraph";
        else if (action == "Velocity")
        {
            if (sender is MenuItem) Dispatcher.UIThread.Post(ShowVelocityFlyout);
            else ShowVelocityFlyout();
            return;
        }
        else if (action is "Symmetry" or "Asymmetry" or "Separately" && DataContext is GraphEditorViewModel model)
        {
            model.Symmetry.Value = action == "Symmetry";
            model.Asymmetry.Value = action == "Asymmetry";
            model.Separately.Value = action == "Separately";
        }
        else if (action == "FitAll") FitGraph(false);
        else if (action == "FitSelection") FitGraph(true);
        else if (action == "Delete") DeleteSelection();
        else if (action is "Ease" or "EaseIn" or "EaseOut" or "Linear" or "Hold") ApplyEasing(action);
        Focus();
    }
}
