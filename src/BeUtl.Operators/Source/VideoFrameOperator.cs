using BeUtl.Graphics.Drawables;
using BeUtl.Streaming;
using BeUtl.Styling;

namespace BeUtl.Operators.Source;

public sealed class VideoFrameOperator : StreamStyledSource
{
    protected override Style OnInitializeStyle(Func<IList<ISetter>> setters)
    {
        var style = new Style<VideoFrame>();
        style.Setters.AddRange(setters());
        return style;
    }

    protected override void OnInitializeSetters(IList<ISetter> initializing)
    {
        initializing.Add(new Setter<TimeSpan>(VideoFrame.OffsetPositionProperty, TimeSpan.Zero));
        initializing.Add(new Setter<TimeSpan>(VideoFrame.PlaybackPositionProperty, TimeSpan.Zero));
        initializing.Add(new Setter<VideoPositionMode>(VideoFrame.PositionModeProperty, VideoPositionMode.Automatic));
        initializing.Add(new Setter<FileInfo?>(VideoFrame.SourceFileProperty, null));
    }
}
