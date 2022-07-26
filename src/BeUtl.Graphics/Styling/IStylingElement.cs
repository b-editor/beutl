namespace BeUtl.Styling;

public interface IStylingElement
{
    IStylingElement? StylingParent { get; }

    IEnumerable<IStylingElement> StylingChildren { get; }

    event EventHandler<StylingTreeAttachmentEventArgs> AttachedToStylingTree;
    event EventHandler<StylingTreeAttachmentEventArgs> DetachedFromStylingTree;

    void NotifyAttachedToStylingTree(in StylingTreeAttachmentEventArgs e);

    void NotifyDetachedFromStylingTree(in StylingTreeAttachmentEventArgs e);
}
