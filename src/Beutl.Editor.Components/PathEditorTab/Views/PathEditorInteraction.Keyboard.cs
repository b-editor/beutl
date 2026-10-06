using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Composition;
using Beutl.Controls;
using Beutl.Editor.Components.PathEditorTab.Services;
using Beutl.Editor.Components.Views;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Media;
using Microsoft.Extensions.DependencyInjection;
using Brushes = Avalonia.Media.Brushes;
using BtlPoint = Beutl.Graphics.Point;
using BtlVector = Beutl.Graphics.Vector;
using CubicBezierSegment = Beutl.Media.CubicBezierSegment;
using LineSegment = Beutl.Media.LineSegment;
using PathFigure = Beutl.Media.PathFigure;
using PathSegment = Beutl.Media.PathSegment;

namespace Beutl.Editor.Components.PathEditorTab.Views;

internal sealed partial class PathEditorInteraction
{
    private void KeyDown(object? sender, KeyEventArgs e)
    {
        if (Ancestor<TextBox>(e.Source) != null || Ancestor<Button>(e.Source) != null) return;
        if (e.Key == Key.Space)
        {
            _space = true;
            UpdateCursor();
            e.Handled = true;
            return;
        }
        var command = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
        if (e.Key == Key.A && e.KeyModifiers == command)
        {
            foreach (Thumb thumb in Anchors) PathPointDragBehavior.SetIsSelected(thumb, true);
            SyncSelection();
            e.Handled = true;
            return;
        }
        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Alt)) != 0) return;
        if (e.Key is Key.Escape or Key.Enter)
        {
            if (e.Key == Key.Escape && (_adding || _bending != null))
            {
                Context?.EditorContext.GetRequiredService<HistoryManager>().Rollback();
                _adding = false;
                _bending = null;
                _view.Refresh();
            }
            FinishGesture();
            Tool = PathEditorTool.Move;
            foreach (Thumb thumb in Anchors) PathPointDragBehavior.SetIsSelected(thumb, false);
            SyncSelection();
            e.Handled = true;
        }
        else if (e.Key is Key.V or Key.P or Key.B or Key.H)
        {
            Tool = e.Key switch { Key.P => PathEditorTool.Pen, Key.B => PathEditorTool.Bend, Key.H => PathEditorTool.Hand, _ => PathEditorTool.Move };
            e.Handled = true;
        }
        else if (e.Key is Key.Delete or Key.Back)
        {
            if (Context?.PathFigure.Value is { } figure)
            {
                var selected = _view.GetSelectedAnchors().Select(t => t.DataContext).OfType<PathSegment>().ToHashSet();
                if (selected.Count > 0)
                {
                    Mutate(() =>
                    {
                        for (int i = figure.Segments.Count - 1; i >= 0; i--)
                            if (selected.Contains(figure.Segments[i])) figure.Segments.RemoveAt(i);
                    });
                    SyncSelection();
                    e.Handled = true;
                }
            }
        }
        else if (e.Key is Key.Left or Key.Up or Key.Right or Key.Down && Context?.PathFigure.Value is { } figure)
        {
            _nudges ??= CreateSelectionDragStates(figure);
            if (_nudges.Count == 0) return;
            float amount = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 10 : 1;
            BtlVector delta = e.Key switch
            {
                Key.Left => new(-amount, 0),
                Key.Right => new(amount, 0),
                Key.Up => new(0, -amount),
                _ => new(0, amount)
            };
            foreach (var state in _nudges) state.Move(delta);
            Context.FigureContext.Value?.InvalidateFrameCache();
            _view.Refresh();
            e.Handled = true;
        }
        else if (_fit != null && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key is Key.D1 or Key.D2)
        {
            _fit(e.Key == Key.D2);
            e.Handled = true;
        }
        else if (_zoom != null && e.Key == Key.D0)
        {
            _zoom(0, default);
            e.Handled = true;
        }
        else if (_zoom != null && e.Key is Key.OemPlus or Key.Add or Key.OemMinus or Key.Subtract)
        {
            _zoom(e.Key is Key.OemPlus or Key.Add ? 1.2 : 1 / 1.2, new Point(_canvas.Bounds.Width / 2, _canvas.Bounds.Height / 2));
            e.Handled = true;
        }
    }

    public void PreviewSelectionPosition(BtlPoint point)
    {
        if (Context?.PathFigure.Value is not { } figure || !float.IsFinite(point.X) || !float.IsFinite(point.Y)) return;
        if (_coordinateStates == null)
        {
            var points = _view.GetSelectedAnchors().Select(PathEditorHelper.GetProperty)
                .Where(p => p != null).Select(p => p!.GetValue(Composition)).ToArray();
            if (points.Length == 0) return;
            CommitNudge();
            Context.EditorContext.GetRequiredService<HistoryManager>().Commit();
            _coordinateContext = Context;
            _coordinateOrigin = new((points.Min(p => p.X) + points.Max(p => p.X)) / 2,
                (points.Min(p => p.Y) + points.Max(p => p.Y)) / 2);
            _coordinateStates = CreateSelectionDragStates(figure);
        }
        BtlVector delta = point - _coordinateOrigin;
        foreach (var state in _coordinateStates) state.MoveFromStart(delta);
        Context.FigureContext.Value?.InvalidateFrameCache();
        _view.Refresh();
    }

    public void CommitSelectionPosition()
    {
        var context = _coordinateContext;
        _coordinateContext = null;
        if (_coordinateStates?.Count > 0)
        {
            _coordinateStates = null;
            context?.EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.EditPathPoint);
        }
        _view.Refresh();
    }

    private List<PathPointDragState> CreateSelectionDragStates(PathFigure figure)
    {
        var states = new List<PathPointDragState>();
        foreach (Thumb thumb in _view.GetSelectedAnchors())
        {
            if (thumb.DataContext is not PathSegment segment || PathEditorHelper.GetProperty(thumb) is not { } property) continue;
            states.Add(PathPointDragBehavior.CreateThumbDragState(Context!, segment, property));
            PathPointDragBehavior.CoordinateControlPoint(states, _view, Context!, figure, segment);
        }
        return states.DistinctBy(s => s.Property).ToList();
    }

    private void KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space) { _space = false; UpdateCursor(); }
        if (e.Key is Key.Left or Key.Up or Key.Right or Key.Down) CommitNudge();
    }

    private void CommitNudge()
    {
        if (_nudges?.Count > 0) Context?.EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.EditPathPoint);
        _nudges = null;
    }

    private void LostFocus(object? sender, RoutedEventArgs e)
    {
        _space = false;
        UpdateCursor();
        CommitNudge();
        CommitSelectionPosition();
    }
}
