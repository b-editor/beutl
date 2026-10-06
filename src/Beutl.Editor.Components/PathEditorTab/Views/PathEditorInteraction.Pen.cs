using Avalonia;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;
using BtlPoint = Beutl.Graphics.Point;
using CubicBezierSegment = Beutl.Media.CubicBezierSegment;
using LineSegment = Beutl.Media.LineSegment;
using PathFigure = Beutl.Media.PathFigure;
using PathSegment = Beutl.Media.PathSegment;

namespace Beutl.Editor.Components.PathEditorTab.Views;

internal sealed partial class PathEditorInteraction
{
    private void PressPen(PointerPressedEventArgs e, PathFigure figure, Thumb? thumb, Point point)
    {
        BtlPoint explicitStart = figure.StartPoint.GetValue(Composition);
        if (_penEnd != null && figure.Segments.Count > 0 && !explicitStart.IsInvalid
            && !PathPointDragBehavior.IsClosed(Context!, figure)
            && ((Vector)(point - Screen(explicitStart))).SquaredLength <= HitRadius * HitRadius)
        {
            CloseAtExplicitStart(figure, explicitStart);
            e.Handled = true;
            return;
        }
        if (thumb?.DataContext is PathSegment endpoint && !thumb.Classes.Contains("control"))
        {
            if (_penEnd != null && explicitStart.IsInvalid && ReferenceEquals(endpoint, figure.Segments.FirstOrDefault())
                && figure.Segments.Count > 1 && !PathPointDragBehavior.IsClosed(Context!, figure))
            {
                CloseImplicitPath(figure, endpoint);
            }
            else if (ReferenceEquals(endpoint, figure.Segments.LastOrDefault()) && !PathPointDragBehavior.IsClosed(Context!, figure))
            {
                _penEnd = endpoint;
                SelectOnly(endpoint);
            }
            e.Handled = true;
            return;
        }
        if (HitEdge(point) is { } hit)
        {
            Insert(hit.index, hit.t);
            e.Handled = true;
            return;
        }
        if (TryLocal(point, out BtlPoint local) && !PathPointDragBehavior.IsClosed(Context!, figure))
        {
            AppendPenPoint(e, figure, explicitStart, point, local);
        }
        e.Handled = true;
    }

    private void CloseAtExplicitStart(PathFigure figure, BtlPoint explicitStart)
    {
        // Reject the whole operation before touching geometry or clearing the
        // pen state when the closing edge cannot preserve its inputs.
        if (!CanCloseExplicitPath(figure)) return;
        Mutate(() =>
        {
            // An explicit start owns the existing first edge. A curved
            // closing edge must be appended, never written over segment 0.
            if (_outgoing is { } outgoing && PathEditingOperations.IsStatic(figure.StartPoint)
                && PathEditingOperations.IsStatic(figure.Segments[^1].GetEndPoint()))
            {
                BtlPoint last = figure.Segments[^1].GetEndPoint().GetValue(Composition);
                figure.Segments.Add(PathEditingOperations.Cubic(outgoing,
                    PathEditingOperations.Lerp(last, explicitStart, 2f / 3), explicitStart));
            }
            figure.IsClosed.CurrentValue = true;
        });
        Tool = PathEditorTool.Move;
    }

    private void CloseImplicitPath(PathFigure figure, PathSegment endpoint)
    {
        if (!CanCloseImplicitPath(figure)) return;
        Mutate(() =>
        {
            if (figure.Segments[0] is CubicBezierSegment closing)
            {
                // The first segment is the closing edge for implicit-start
                // figures. Keep its identity and any untouched handle data.
                if (_outgoing is { } outgoing) closing.ControlPoint1.CurrentValue = outgoing;
                if (_firstIncoming is { } incoming) closing.ControlPoint2.CurrentValue = incoming;
            }
            else if (figure.Segments[0] is LineSegment && (_firstIncoming != null || _outgoing != null))
            {
                BtlPoint last = figure.Segments[^1].GetEndPoint().GetValue(Composition);
                BtlPoint first = endpoint.GetEndPoint().GetValue(Composition);
                figure.Segments[0] = PathEditingOperations.Cubic(
                    _outgoing ?? PathEditingOperations.Lerp(last, first, 1f / 3),
                    _firstIncoming ?? PathEditingOperations.Lerp(last, first, 2f / 3), first);
            }
            figure.IsClosed.CurrentValue = true;
        });
        Tool = PathEditorTool.Move;
    }

    private void AppendPenPoint(PointerPressedEventArgs e, PathFigure figure, BtlPoint explicitStart, Point point, BtlPoint local)
    {
        // New controls are static. Do not sample a driven edge start into
        // them, whether it is the explicit start or a previous endpoint.
        var startProperty = figure.Segments.Count == 0
            ? figure.StartPoint : figure.Segments[^1].GetEndPoint();
        if (!PathEditingOperations.IsStatic(startProperty)) return;
        _penPoint = local;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && figure.Segments.Count > 0)
            _penPoint = PathEditingOperations.SnapAngle(figure.Segments[^1].GetEndPoint().GetValue(Composition), local);
        _penSegment = null;
        _start = point;
        Context!.EditorContext.GetRequiredService<HistoryManager>().Commit();
        // Only an implicit start needs a move-only anchor. With an explicit
        // start, the first point already terminates an edge that can be bent.
        if (figure.Segments.Count == 0 && explicitStart.IsInvalid)
        {
            _penEnd = new LineSegment(_penPoint);
            figure.Segments.Add(_penEnd);
        }
        else
        {
            BtlPoint previous = figure.Segments.Count == 0
                ? explicitStart : figure.Segments[^1].GetEndPoint().GetValue(Composition);
            _penSegment = PathEditingOperations.Cubic(_outgoing ?? PathEditingOperations.Lerp(previous, _penPoint, 1f / 3),
                PathEditingOperations.Lerp(previous, _penPoint, 2f / 3), _penPoint);
            _penEnd = _penSegment;
            figure.Segments.Add(_penSegment);
        }
        _outgoing = null;
        SelectOnly(_penEnd);
        _adding = true;
        Capture(e);
    }

    private bool CanCloseExplicitPath(PathFigure figure) =>
        PathEditingOperations.IsStatic(figure.IsClosed)
        && (_outgoing == null || PathEditingOperations.IsStatic(figure.StartPoint)
            && PathEditingOperations.IsStatic(figure.Segments[^1].GetEndPoint()));

    private bool CanCloseImplicitPath(PathFigure figure)
    {
        if (!PathEditingOperations.IsStatic(figure.IsClosed)) return false;
        if (_outgoing == null && _firstIncoming == null) return true;
        if (!PathEditingOperations.IsStatic(figure.StartPoint)
            || !PathEditingOperations.IsStatic(figure.Segments[0].GetEndPoint())
            || !PathEditingOperations.IsStatic(figure.Segments[^1].GetEndPoint())) return false;
        return figure.Segments[0] switch
        {
            LineSegment => true,
            CubicBezierSegment cubic => (_outgoing == null || PathEditingOperations.IsStatic(cubic.ControlPoint1))
                && (_firstIncoming == null || PathEditingOperations.IsStatic(cubic.ControlPoint2)),
            _ => false
        };
    }
}
