namespace BeUtl.Styling;

public readonly struct StylingTreeAttachmentEventArgs
{
    public StylingTreeAttachmentEventArgs(IStylingElement? parent)
    {
        Parent = parent;
    }

    public IStylingElement? Parent { get; }
}
