namespace BeUtl;

public readonly struct LogicalTreeAttachmentEventArgs
{
    public LogicalTreeAttachmentEventArgs(ILogicalElement? parent)
    {
        Parent = parent;
    }

    public ILogicalElement? Parent { get; }
}
