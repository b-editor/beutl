using Beutl.Graphics.Drawables;
using Beutl.Operation;
using Beutl.Styling;

namespace Beutl.Operators.Source;

public sealed class VideoFrameOperator : DrawablePublishOperator<VideoFrame>
{
    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<TimeSpan>(VideoFrame.OffsetPositionProperty, TimeSpan.Zero));
        initializing.Add(new Setter<TimeSpan>(VideoFrame.PlaybackPositionProperty, TimeSpan.Zero));
        initializing.Add(new Setter<VideoPositionMode>(VideoFrame.PositionModeProperty, VideoPositionMode.Automatic));
        initializing.Add(new Setter<FileInfo?>(VideoFrame.SourceFileProperty, null));
    }
}
