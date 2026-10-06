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
    private void Bend(PathFigure figure, PathSegment anchor)
    {
        int index = figure.Segments.IndexOf(anchor);
        if (index < 0) return;
        var composition = Composition;
        int first = PathEditingOperations.FirstEdge(figure, composition);
        int next = index + 1;
        if (next == figure.Segments.Count && figure.IsClosed.GetValue(composition)
            && figure.StartPoint.GetValue(composition).IsInvalid) next = 0;
        if (index >= first && !CanBendEdge(index, incoming: true)
            || next < figure.Segments.Count && !CanBendEdge(next, incoming: false)) return;
        BtlPoint point = anchor.GetEndPoint().GetValue(composition);
        // Sub-pixel differences must not turn an apparently collapsed corner into
        // a "remove handles" click. The inspector rounds these coordinates too.
        bool hasHandles = PathPointProperties.Get(figure, anchor, composition)
            .Any(p => p.Role != PathPointPropertyRole.Position
                && ((Vector)(Screen(p.Property.GetValue(composition)) - Screen(point))).SquaredLength > 1);
        PathSegment selected = anchor;
        Mutate(() =>
        {
            BtlPoint before = index >= first ? PathEditingOperations.Start(figure, index, composition) : point;
            BtlPoint after = next < figure.Segments.Count ? figure.Segments[next].GetEndPoint().GetValue(composition) : point;
            var tangent = after - before;
            float length = MathF.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y);
            if (length == 0)
            {
                // At a turnaround (including a two-point closed path), the two
                // neighbors coincide. Use the perpendicular instead of cancelling out.
                var chord = after - point;
                tangent = new(-chord.Y, chord.X);
                length = MathF.Sqrt(tangent.X * tangent.X + tangent.Y * tangent.Y);
                if (length == 0) { tangent = new(1, 0); length = 1; }
            }
            if (index >= first && GetCubic(index) is { } c)
            {
                selected = c;
                float radius = ((BtlVector)(point - before)).Length / 3;
                SetHandle(c, c.ControlPoint2, hasHandles ? point : point - tangent * (radius / length));
            }
            if (next < figure.Segments.Count && GetCubic(next) is { } following)
            {
                float radius = ((BtlVector)(after - point)).Length / 3;
                SetHandle(following, following.ControlPoint1, hasHandles ? point : point + tangent * (radius / length));
            }
        });
        SelectOnly(selected);

        bool CanBendEdge(int edge, bool incoming)
        {
            if (figure.Segments[edge] is CubicBezierSegment cubic)
            {
                IProperty<BtlPoint> property = incoming ? cubic.ControlPoint2 : cubic.ControlPoint1;
                return !property.HasExpression && property.Animation is null or KeyFrameAnimation<BtlPoint>;
            }
            return figure.Segments[edge] is LineSegment or Beutl.Media.QuadraticBezierSegment
                && PathEditingOperations.CanSplit(figure, edge, preserveSingleKeyframe: false);
        }

        CubicBezierSegment? GetCubic(int edge) => figure.Segments[edge] as CubicBezierSegment
            ?? PathEditingOperations.ToCubic(figure, edge, composition);

        void SetHandle(CubicBezierSegment segment, IProperty<BtlPoint> property, BtlPoint value)
        {
            // Editing existing handles does not replace segments or bake their animation.
            var state = PathPointDragBehavior.CreateThumbDragState(Context!, segment, property);
            if (state.Previous == null && state.Next == null) property.CurrentValue = value;
            else state.Move((BtlVector)(value - property.GetValue(composition)));
        }
    }
}
