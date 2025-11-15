using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media.Source;
using Beutl.Operation;

namespace Beutl.Operators.Source;

public sealed class SourceVideoOperator : PublishOperator<SourceVideo>
{
    private string? _sourceName;

    protected override void FillProperties()
    {
        AddProperty(Value.OffsetPosition, TimeSpan.Zero);
        AddProperty(Value.Source);
        AddProperty(Value.Transform, new TransformGroup());
        AddProperty(Value.AlignmentX);
        AddProperty(Value.AlignmentY);
        AddProperty(Value.TransformOrigin);
        AddProperty(Value.FilterEffect, new FilterEffectGroup());
        AddProperty(Value.BlendMode);
        AddProperty(Value.Opacity);
    }

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        if (Value is not { Source.CurrentValue: { Name: { } name } source } value) return;

        _sourceName = name;
        value.Source.CurrentValue = null;
        source.Dispose();
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        if (_sourceName is null) return;
        if (Value is not { } value) return;

        if (VideoSource.TryOpen(_sourceName, out VideoSource? source))
        {
            value.Source.CurrentValue = source;
        }
    }

    public override bool HasOriginalLength()
    {
        return Value?.Source.CurrentValue?.IsDisposed == false;
    }

    public override bool TryGetOriginalLength(out TimeSpan timeSpan)
    {
        var ts = Value.CalculateOriginalTime();
        if (ts.HasValue)
        {
            timeSpan = ts.Value - Value.OffsetPosition.CurrentValue;
            return true;
        }
        else
        {
            timeSpan = TimeSpan.Zero;
            return false;
        }
    }

    public override IRecordableCommand? OnSplit(bool backward, TimeSpan startDelta, TimeSpan lengthDelta)
    {
        if (Value is null) return null;

        if (backward)
        {
            TimeSpan newValue = Value.OffsetPosition.CurrentValue + startDelta;
            TimeSpan oldValue = Value.OffsetPosition.CurrentValue;

            return RecordableCommands.Create([this])
                .OnDo(() => Value.OffsetPosition.CurrentValue = newValue)
                .OnUndo(() => Value.OffsetPosition.CurrentValue = oldValue)
                .ToCommand();
        }
        else
        {
            return base.OnSplit(backward, startDelta, lengthDelta);
        }
    }
}
