using Avalonia.Controls.Primitives;

using Beutl.Animation;
using Beutl.Composition;
using Beutl.Engine;
using Beutl.Media;

using BtlPoint = Beutl.Graphics.Point;
using BtlVector = Beutl.Graphics.Vector;

namespace Beutl.Editor.Components.PathEditorTab.Views;

public sealed class PathPointDragState
{
    public PathPointDragState(
        IExpressionProperty<BtlPoint> property,
        PathSegment target,
        KeyFrame<BtlPoint>? previous,
        KeyFrame<BtlPoint>? next)
    {
        Previous = previous;
        Next = next;
        Property = property;
        Target = target;
        OldPreviousValue = previous?.Value ?? default;
        OldNextValue = next?.Value ?? default;
        OldValue = property.CurrentValue;
        Animation = property.GetKeyFrameAnimation() as KeyFrameAnimation<BtlPoint>;
    }

    public KeyFrameAnimation<BtlPoint>? Animation { get; }

    public KeyFrame<BtlPoint>? Previous { get; }

    public KeyFrame<BtlPoint>? Next { get; }

    public Thumb? Thumb { get; set; }

    public IExpressionProperty<BtlPoint> Property { get; }

    public PathSegment Target { get; }

    public BtlPoint OldPreviousValue { get; }

    public BtlPoint OldNextValue { get; }

    public BtlPoint OldValue { get; }

    public BtlPoint GetInterpolatedValue(TimeSpan currentTime)
    {
        var ctx = new CompositionContext(currentTime);
        return Property.GetValue(ctx);
    }

    public void MoveFromStart(BtlVector delta)
    {
        if (Previous == null && Next == null)
            Property.CurrentValue = PathEditorHelper.Round(OldValue + delta);
        else
        {
            if (Previous != null) Previous.Value = OldPreviousValue + delta;
            if (Next != null) Next.Value = OldNextValue + delta;
        }
    }

    public void Move(BtlVector delta)
    {
        if (Previous == null && Next == null)
        {
            BtlPoint p = Property.CurrentValue + delta;
            p = PathEditorHelper.Round(p);

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
