namespace BEditorNext.ProjectSystem;

public class CurrentFrameChangedEventArgs : EventArgs
{
    public CurrentFrameChangedEventArgs(TimeSpan oldFrame, TimeSpan newFrame)
    {
        OldFrame = oldFrame;
        NewFrame = newFrame;
    }

    public TimeSpan OldFrame { get; }

    public TimeSpan NewFrame { get; }
}
