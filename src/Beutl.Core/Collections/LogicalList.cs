namespace Beutl.Collections;

public class LogicalList<T> : CoreList<T>
    where T : ILogicalElement
{
    public LogicalList(ILogicalElement parent)
    {
        Parent = parent;
        Attached += item => item.NotifyAttachedToLogicalTree(new LogicalTreeAttachmentEventArgs(Parent));
        Detached += item => item.NotifyDetachedFromLogicalTree(new LogicalTreeAttachmentEventArgs(Parent));
    }

    public ILogicalElement Parent { get; }
}
