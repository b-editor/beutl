using System.Collections.Immutable;

using Avalonia.Controls.Primitives;

using Beutl.Animation;
using Beutl.Media;

using BtlPoint = Beutl.Graphics.Point;
using BtlVector = Beutl.Graphics.Vector;

namespace Beutl.Views;

public sealed class PathPointDragState
{
    public PathPointDragState(
        CoreProperty<BtlPoint> property,
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
        OldValue = target.GetValue(property);
        Animation = Target.Animations.FirstOrDefault(a => a.Property == Property) as KeyFrameAnimation<BtlPoint>;
    }

    public KeyFrameAnimation<BtlPoint>? Animation { get; }

    public KeyFrame<BtlPoint>? Previous { get; }

    public KeyFrame<BtlPoint>? Next { get; }

    public Thumb? Thumb { get; set; }

    public CoreProperty<BtlPoint> Property { get; }

    public PathSegment Target { get; }

    public PathSegment? Anchor { get; set; }

    public BtlPoint OldPreviousValue { get; }

    public BtlPoint OldNextValue { get; }

    public BtlPoint OldValue { get; }

    public BtlPoint GetSampleValue()
    {
        if (Previous != null)
        {
            return Previous.GetValue(KeyFrame<BtlPoint>.ValueProperty);
        }
        else
        {
            return Target.GetValue(Property);
        }
    }

    public BtlPoint GetInterpolatedValue(ProjectSystem.Element element, TimeSpan currentTime)
    {
        if (Animation != null)
        {
            if (Animation.UseGlobalClock)
            {
                return Animation.Interpolate(currentTime);
            }
            else
            {
                return Animation.Interpolate(currentTime - element.Start);
            }
        }
        else
        {
            return Target.GetValue(Property);
        }
    }

    public void SetValue(BtlPoint point)
    {
        if (Previous == null && Next == null)
        {
            Target.SetValue(Property, point);
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
            BtlPoint p = Target.GetValue(Property) + delta;
            p = new BtlPoint(PathEditorHelper.Round(p.X), PathEditorHelper.Round(p.Y));

            Target.SetValue(Property, p);
        }
        else
        {
            CoreProperty<BtlPoint> prop = KeyFrame<BtlPoint>.ValueProperty;
            Previous?.SetValue(prop, Previous.GetValue(prop) + delta);

            Next?.SetValue(prop, Next.GetValue(prop) + delta);
        }
    }

    public IRecordableCommand? CreateCommand(ImmutableArray<IStorable?> storables)
    {
        if (Previous == null && Next == null)
        {
            return RecordableCommands.Edit(Target, Property, Target.GetValue(Property), OldValue)
                .WithStoables(storables);
        }
        else
        {
            return RecordableCommands.Append(
                Previous != null && Previous.Value != OldPreviousValue
                    ? RecordableCommands.Edit(Previous, KeyFrame<BtlPoint>.ValueProperty, Previous.Value, OldPreviousValue).WithStoables(storables)
                    : null,
                Next != null && Next.Value != OldNextValue
                    ? RecordableCommands.Edit(Next, KeyFrame<BtlPoint>.ValueProperty, Next.Value, OldNextValue).WithStoables(storables)
                    : null);
        }
    }
}
