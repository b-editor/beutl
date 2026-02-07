namespace Beutl.Graphics.Backend;

/// <summary>
/// Specifies the operation to perform on an attachment at the beginning of a render pass.
/// </summary>
public enum AttachmentLoadOp
{
    /// <summary>
    /// The previous contents of the attachment are preserved.
    /// </summary>
    Load = 0,

    /// <summary>
    /// The previous contents of the attachment are cleared.
    /// </summary>
    Clear = 1,

    /// <summary>
    /// The previous contents of the attachment are undefined.
    /// </summary>
    DontCare = 2
}
