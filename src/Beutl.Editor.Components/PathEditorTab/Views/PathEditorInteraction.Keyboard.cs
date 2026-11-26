using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Components.Helpers;
using Microsoft.Extensions.DependencyInjection;
using static Avalonia.Input.KeyModifiers;
using static Beutl.Editor.Components.Helpers.EditorShortcutGestures;
using BtlPoint = Beutl.Graphics.Point;
using BtlVector = Beutl.Graphics.Vector;
using PathFigure = Beutl.Media.PathFigure;
using PathSegment = Beutl.Media.PathSegment;

namespace Beutl.Editor.Components.PathEditorTab.Views;

internal sealed partial class PathEditorInteraction
{
    private readonly HashSet<Key> _nudgeKeys = [];

    internal static IReadOnlyList<ContextCommandDefinition> Commands =>
    [
        Shortcut("pan", Strings.PanView, Gestures(Key.Space, optional: AllModifiers), (path, key) =>
        {
            // PathPointDragBehavior runs a thumb's drag, and SkipUpdatePosition is what shows it.
            path._space.Press(key, path._pointer == null && !path._view.SkipUpdatePosition);
            path.UpdateCursor();
        }),
        Shortcut("selectAll", Strings.SelectAll, WithCommand(Key.A), path => path.SelectAllAnchors()),
        Shortcut("cancel", Strings.Cancel, Gestures(Key.Escape, optional: Shift), path => path.ExitTool(rollback: true)),
        Shortcut("finish", Strings.FinishEditing, Gestures(Key.Enter, optional: Shift), path => path.ExitTool(rollback: false)),
        SelectTool("move", Strings.PathEditor_MoveTool, Key.V, PathEditorTool.Move),
        SelectTool("pen", Strings.PathEditor_PenTool, Key.P, PathEditorTool.Pen),
        SelectTool("bend", Strings.PathEditor_BendTool, Key.B, PathEditorTool.Bend),
        SelectTool("hand", Strings.PathEditor_HandTool, Key.H, PathEditorTool.Hand),
        Shortcut("delete", Strings.Delete, [.. Gestures(Key.Delete, optional: Shift), .. Gestures(Key.Back, optional: Shift)],
            (path, key) => key.Handled = path.DeleteSelectedAnchors()),
        Nudge("nudgeLeft", Strings.PathEditor_NudgeLeft, Key.Left, new(-1, 0), 1),
        Nudge("nudgeRight", Strings.PathEditor_NudgeRight, Key.Right, new(1, 0), 1),
        Nudge("nudgeUp", Strings.PathEditor_NudgeUp, Key.Up, new(0, -1), 1),
        Nudge("nudgeDown", Strings.PathEditor_NudgeDown, Key.Down, new(0, 1), 1),
        Nudge("nudgeLeftLarge", Strings.PathEditor_NudgeLeft, Key.Left, new(-1, 0), 10),
        Nudge("nudgeRightLarge", Strings.PathEditor_NudgeRight, Key.Right, new(1, 0), 10),
        Nudge("nudgeUpLarge", Strings.PathEditor_NudgeUp, Key.Up, new(0, -1), 10),
        Nudge("nudgeDownLarge", Strings.PathEditor_NudgeDown, Key.Down, new(0, 1), 10),
        Shortcut("fitAll", Strings.GraphFitAll, Gestures(Key.D1, Shift), path => path._fit!(false),
            path => path._fit is not null),
        Shortcut("fitSelection", Strings.GraphFitSelection, Gestures(Key.D2, Shift), path => path._fit!(true),
            path => path._fit is not null),
        Shortcut("resetZoom", Strings.ResetZoom, Gestures(Key.D0, optional: Shift), path => path._zoom!(0, default),
            path => path._zoom is not null),
        // Avalonia matches the keypad Add/Subtract keys to OemPlus/OemMinus as well.
        Zoom("zoomIn", Strings.PathEditor_ZoomIn, Key.OemPlus, 1.2),
        Zoom("zoomOut", Strings.PathEditor_ZoomOut, Key.OemMinus, 1 / 1.2)
    ];

    private static ContextCommandDefinition Shortcut(string id, string name, ContextCommandKeyGesture[] gestures,
        Action<PathEditorInteraction> run, Func<PathEditorInteraction, bool>? canExecute = null)
        => Shortcut(id, name, gestures, (path, _) => run(path), canExecute);

    private static ContextCommandDefinition Shortcut(string id, string name, ContextCommandKeyGesture[] gestures,
        Action<PathEditorInteraction, KeyEventArgs> run, Func<PathEditorInteraction, bool>? canExecute = null)
        => new ContextCommandDefinition<PathEditorInteraction>($"pathEditor.{id}", name, "", gestures)
        {
            CanExecuteOn = (path, execution) => execution.KeyEventArgs is not null && (canExecute?.Invoke(path) ?? true),
            ExecuteOnAsync = (path, execution) =>
            {
                run(path, execution.KeyEventArgs!);
                return Task.CompletedTask;
            }
        };

    private static ContextCommandDefinition SelectTool(string id, string name, Key key, PathEditorTool tool)
        => Shortcut(id, name, Gestures(key, optional: Shift), path => path.Tool = tool);

    private static ContextCommandDefinition Nudge(string id, string name, Key key, BtlVector direction, int amount)
        => Shortcut(id, string.Format(name, amount), Gestures(key, amount == 1 ? None : Shift),
            (path, args) =>
            {
                args.Handled = path.NudgeSelection(direction * amount);
                if (args.Handled) path._nudgeKeys.Add(args.Key);
            });

    private static ContextCommandDefinition Zoom(string id, string name, Key key, double ratio)
        => Shortcut(id, name, Gestures(key, optional: Shift),
            path => path._zoom!(ratio, new Point(path._canvas.Bounds.Width / 2, path._canvas.Bounds.Height / 2)),
            path => path._zoom is not null);

    private void KeyDown(object? sender, KeyEventArgs e)
    {
        if (Ancestor<TextBox>(e.Source) != null || Ancestor<Button>(e.Source) != null) return;
        if (_space.HandleRepeat(e)) return;

        AppHelper.GetContextCommandManager?.Invoke()?.HandleKeyDown(PathEditorTabExtension.Instance, this, e);
    }

    private void SelectAllAnchors()
    {
        foreach (Thumb thumb in Anchors) PathPointDragBehavior.SetIsSelected(thumb, true);
        SyncSelection();
    }

    private void ExitTool(bool rollback)
    {
        if (rollback && (_adding || _bending != null))
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
    }

    private bool DeleteSelectedAnchors()
    {
        if (Context?.PathFigure.Value is not { } figure) return false;
        var selected = _view.GetSelectedAnchors().Select(t => t.DataContext).OfType<PathSegment>().ToHashSet();
        if (selected.Count == 0) return false;

        // A read-only path keeps its points, and the key still stops here.
        if (CanEdit)
        {
            Mutate(() =>
            {
                for (int i = figure.Segments.Count - 1; i >= 0; i--)
                    if (selected.Contains(figure.Segments[i])) figure.Segments.RemoveAt(i);
            });
            SyncSelection();
        }
        return true;
    }

    private bool NudgeSelection(BtlVector delta)
    {
        if (!CanEdit || Context?.PathFigure.Value is not { } figure) return false;
        _nudges ??= CreateSelectionDragStates(figure);
        if (_nudges.Count == 0) return false;
        foreach (var state in _nudges) state.Move(delta);
        Context!.FigureContext.Value?.InvalidateFrameCache();
        _view.Refresh();
        return true;
    }

    public void PreviewSelectionPosition(BtlPoint point)
    {
        if (!CanEdit || Context?.PathFigure.Value is not { } figure || !float.IsFinite(point.X) || !float.IsFinite(point.Y)) return;
        if (_coordinateStates == null)
        {
            var points = _view.GetSelectedAnchors().Select(PathEditorHelper.GetProperty)
                .Where(p => p != null).Select(p => p!.Evaluate(Composition)).ToArray();
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
        if (_space.Release(e)) UpdateCursor();
        if (_nudgeKeys.Remove(e.Key)) CommitNudge();
    }

    private void CommitNudge()
    {
        if (_nudges?.Count > 0) Context?.EditorContext.GetRequiredService<HistoryManager>().Commit(CommandNames.EditPathPoint);
        _nudges = null;
    }

    private void LostFocus(object? sender, RoutedEventArgs e)
    {
        _space.Reset();
        UpdateCursor();
        CommitNudge();
        _nudgeKeys.Clear();
        CommitSelectionPosition();
    }
}
