namespace Beutl.Graphics.Effects;

/// <summary>
/// A Skia item recorded against symbolic input bounds: only the target bounds of the activation that runs
/// it can fix its mapping.
/// </summary>
internal interface IFEItem_DeferredBounds : IFEItem_Skia
{
    /// <summary>
    /// Returns this item with its mapping fixed from <paramref name="targetBounds"/>, the combined
    /// execution-time target bounds of one activation.
    /// </summary>
    /// <remarks>
    /// The resolution is handed back rather than stored here. One recorded item is shared by every
    /// activation of the context that holds it and of every shallow clone of that context, so a
    /// resolution kept on the item would report the first activation's mapping for all of them.
    /// </remarks>
    IFEItem_Skia ResolveForActivation(Rect targetBounds);
}
