using BeUtl.Collections;

namespace BeUtl.Styling;

public class StylingElements<T> : CoreList<T>
    where T : IStylingElement
{
    public StylingElements(IStylingElement parent)
    {
        Parent = parent;
        Attached += item => item.NotifyAttachedToStylingTree(new StylingTreeAttachmentEventArgs(Parent));
        Detached += item => item.NotifyDetachedFromStylingTree(new StylingTreeAttachmentEventArgs(Parent));
    }

    public IStylingElement Parent { get; }
}
