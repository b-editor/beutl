namespace BEditorNext.ProjectSystem;

public class CurrentFrameChangedEventArgs : EventArgs
{
    public CurrentFrameChangedEventArgs(int oldFrame, int newFrame)
    {
        OldFrame = oldFrame;
        NewFrame = newFrame;
    }

    public int OldFrame { get; }

    public int NewFrame { get; }
}
