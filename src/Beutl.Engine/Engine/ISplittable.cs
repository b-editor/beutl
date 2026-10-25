namespace Beutl.Engine;

public interface ISplittable
{
    void NotifySplitted(bool backward, MediaTime startDelta, MediaTime durationDelta);
}
