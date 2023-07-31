using Beutl.Graphics.Drawables;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class VideoFrameOperator : DrawablePublishOperator<VideoFrame>
{
    public Setter<TimeSpan> OffsetPosition { get; set; } = new Setter<TimeSpan>(VideoFrame.OffsetPositionProperty, TimeSpan.Zero);

    public Setter<TimeSpan> PlaybackPosition { get; set; } = new Setter<TimeSpan>(VideoFrame.PlaybackPositionProperty, TimeSpan.Zero);

    public Setter<VideoPositionMode> PositionMode { get; set; } = new Setter<VideoPositionMode>(VideoFrame.PositionModeProperty, VideoPositionMode.Automatic);

    public Setter<FileInfo?> SourceFile { get; set; } = new Setter<FileInfo?>(VideoFrame.SourceFileProperty, null);
}
