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
