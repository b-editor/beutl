namespace Beutl.Engine;

public interface ISplittable
{
    void NotifySplitted(bool backward, TimeSpan startDelta, TimeSpan durationDelta);
}
