using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media.Source;
using Beutl.Operation;

namespace Beutl.Operators.Source;

public sealed class SourceVideoOperator() : PublishOperator<SourceVideo>(
[
    (SourceVideo.OffsetPositionProperty, TimeSpan.Zero),
    (SourceVideo.PlaybackPositionProperty, TimeSpan.Zero),
    (SourceVideo.PositionModeProperty, VideoPositionMode.Automatic),
    SourceVideo.SourceProperty,
    (Drawable.TransformProperty, () => new TransformGroup()),
    Drawable.AlignmentXProperty,
    Drawable.AlignmentYProperty,
    Drawable.TransformOriginProperty,
    (Drawable.FilterEffectProperty, () => new FilterEffectGroup()),
    Drawable.BlendModeProperty,
    Drawable.OpacityProperty
])
{
    private string? _sourceName;

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        if (Value is not { Source: { Name: { } name } source } value) return;

        _sourceName = name;
        value.Source = null;
        source.Dispose();
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        if (_sourceName is null) return;
        if (Value is not { } value) return;

        if (VideoSource.TryOpen(_sourceName, out VideoSource? source))
        {
            value.Source = source;
        }
    }

    public override bool HasOriginalLength()
    {
        return Value?.Source?.IsDisposed == false;
    }

    public override bool TryGetOriginalLength(out TimeSpan timeSpan)
    {
        if (Value?.Source?.IsDisposed == false)
        {
            timeSpan = Value.Source.Duration - Value.OffsetPosition;
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
            IStorable? storable = this.FindHierarchicalParent<IStorable>();
            TimeSpan newValue = Value.OffsetPosition + startDelta;
            TimeSpan oldValue = Value.OffsetPosition;

            return RecordableCommands.Create([storable])
                .OnDo(() => Value.OffsetPosition = newValue)
                .OnUndo(() => Value.OffsetPosition = oldValue)
                .ToCommand();
        }
        else
        {
            return base.OnSplit(backward, startDelta, lengthDelta);
        }
    }
}
