using Beutl.Graphics;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Transformation;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class SourceVideoOperator : DrawablePublishOperator<SourceVideo>
{
    private string? _sourceName;

    public Setter<TimeSpan> OffsetPosition { get; set; } = new Setter<TimeSpan>(SourceVideo.OffsetPositionProperty, TimeSpan.Zero);

    public Setter<TimeSpan> PlaybackPosition { get; set; } = new Setter<TimeSpan>(SourceVideo.PlaybackPositionProperty, TimeSpan.Zero);

    public Setter<VideoPositionMode> PositionMode { get; set; } = new Setter<VideoPositionMode>(SourceVideo.PositionModeProperty, VideoPositionMode.Automatic);

    public Setter<IVideoSource?> Source { get; set; } = new(SourceVideo.SourceProperty, null);

    public Setter<ITransform?> Transform { get; set; } = new(Drawable.TransformProperty, new TransformGroup());

    public Setter<AlignmentX> AlignmentX { get; set; } = new(Drawable.AlignmentXProperty, Media.AlignmentX.Center);

    public Setter<AlignmentY> AlignmentY { get; set; } = new(Drawable.AlignmentYProperty, Media.AlignmentY.Center);

    public Setter<RelativePoint> TransformOrigin { get; set; } = new(Drawable.TransformOriginProperty, RelativePoint.Center);

    public Setter<IBrush?> Fill { get; set; } = new(Drawable.FillProperty, new SolidColorBrush(Colors.White));

    public Setter<FilterEffect?> FilterEffect { get; set; } = new(Drawable.FilterEffectProperty, new FilterEffectGroup());

    public Setter<BlendMode> BlendMode { get; set; } = new Setter<BlendMode>(Drawable.BlendModeProperty, Graphics.BlendMode.SrcOver);

    protected override void OnDetachedFromHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnDetachedFromHierarchy(args);
        if (Source is { Value: { Name: string name } value } setter)
        {
            _sourceName = name;
            setter.Value = null;
            value.Dispose();
        }
    }

    protected override void OnAttachedToHierarchy(in HierarchyAttachmentEventArgs args)
    {
        base.OnAttachedToHierarchy(args);
        if (Source is { Value: null } setter
            && _sourceName != null
            && VideoSource.TryOpen(_sourceName, out VideoSource? videoSource))
        {
            setter.Value = videoSource;
        }
    }

    public override bool HasOriginalLength()
    {
        return Source.Value?.IsDisposed == false;
    }

    public override bool TryGetOriginalLength(out TimeSpan timeSpan)
    {
        if (Source.Value?.IsDisposed == false)
        {
            timeSpan = Source.Value.Duration - OffsetPosition.Value;
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
        if (backward)
        {
            return new ChangeSetterValueCommand<TimeSpan>(OffsetPosition, OffsetPosition.Value, OffsetPosition.Value + startDelta);
        }
        else
        {
            return base.OnSplit(backward, startDelta, lengthDelta);
        }
    }
}
