namespace Beutl.Engine;

public interface ISplittable
{
    /// <summary>Notifies the object that its element was split.</summary>
    /// <param name="backward">Whether this is the back half.</param>
    /// <param name="startDelta">The source time the front half played, already mapped through Speed and time controllers.</param>
    /// <param name="durationDelta">The change to this half's timeline duration.</param>
    void NotifySplitted(bool backward, MediaTime startDelta, MediaTime durationDelta);
}
