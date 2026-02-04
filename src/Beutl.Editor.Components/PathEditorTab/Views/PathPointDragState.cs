using System.Collections.Immutable;

using Avalonia.Controls.Primitives;

using Beutl.Animation;
using Beutl.Engine;
using Beutl.Graphics.Rendering;
using Beutl.Media;

using BtlPoint = Beutl.Graphics.Point;
using BtlVector = Beutl.Graphics.Vector;

namespace Beutl.Editor.Components.PathEditorTab.Views;

public sealed class PathPointDragState
{
    public PathPointDragState(
        IProperty<BtlPoint> property,
        PathSegment target,
        KeyFrame<BtlPoint>? previous,
        KeyFrame<BtlPoint>? next,
        // このThumbがControlPointの時、点線でつながっているポイントを指定する
        PathSegment? anchor = null)
    {
        Previous = previous;
        Next = next;
        Anchor = anchor;
        Property = property;
        Target = target;
        OldPreviousValue = previous?.Value ?? default;
        OldNextValue = next?.Value ?? default;
        OldValue = property.CurrentValue;
        Animation = property.Animation as KeyFrameAnimation<BtlPoint>;
    }

    public KeyFrameAnimation<BtlPoint>? Animation { get; }

    public KeyFrame<BtlPoint>? Previous { get; }

    public KeyFrame<BtlPoint>? Next { get; }

    public Thumb? Thumb { get; set; }

    public IProperty<BtlPoint> Property { get; }

    public PathSegment Target { get; }

    public PathSegment? Anchor { get; set; }

    public BtlPoint OldPreviousValue { get; }

    public BtlPoint OldNextValue { get; }

    public BtlPoint OldValue { get; }

    public BtlPoint GetSampleValue(TimeSpan currentTime)
    {
        if (Previous != null)
        {
            return Previous.GetValue(KeyFrame<BtlPoint>.ValueProperty);
        }
        else
        {
            var ctx = new RenderContext(currentTime);
            return Property.GetValue(ctx);
        }
    }

    public BtlPoint GetInterpolatedValue(TimeSpan currentTime)
    {
        var ctx = new RenderContext(currentTime);
        return Property.GetValue(ctx);
    }

    public void SetValue(BtlPoint point)
    {
        if (Previous == null && Next == null)
        {
            Property.CurrentValue = point;
        }
        else
        {
            CoreProperty<BtlPoint> prop = KeyFrame<BtlPoint>.ValueProperty;

            Previous?.SetValue(prop, point);
        }
    }

    public void Move(BtlVector delta)
    {
        if (Previous == null && Next == null)
        {
            BtlPoint p = Property.CurrentValue + delta;
            p = new BtlPoint(PathEditorHelper.Round(p.X), PathEditorHelper.Round(p.Y));

            Property.CurrentValue = p;
        }
        else
        {
            CoreProperty<BtlPoint> prop = KeyFrame<BtlPoint>.ValueProperty;
            Previous?.SetValue(prop, Previous.GetValue(prop) + delta);

            Next?.SetValue(prop, Next.GetValue(prop) + delta);
        }
    }
}
