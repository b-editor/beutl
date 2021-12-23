namespace BEditorNext;

public readonly struct LogicalTreeAttachmentEventArgs
{
    public LogicalTreeAttachmentEventArgs(ILogicalElement? oldParent, ILogicalElement? newParent)
    {
        NewParent = newParent;
        OldParent = oldParent;
    }

    public ILogicalElement? NewParent { get; }

    public ILogicalElement? OldParent { get; }
}
