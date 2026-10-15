using Beutl.Collections;

namespace Beutl.Animation;

public class KeyFrames : HierarchicalList<IKeyFrame>
{
    public KeyFrames(IModifiableHierarchical parent) : base(parent)
    {
    }

    public KeyFrames()
    {
    }

    // Inserts at the position that keeps the keyframes in KeyTime order; Add appends.
    public void AddSorted(IKeyFrame item)
    {
        Insert(IndexAtOrCount(item.KeyTime), item);
    }

    public (IKeyFrame? Previous, IKeyFrame? Next) GetPreviousAndNextKeyFrame(IKeyFrame keyframe)
    {
        int index = IndexOf(keyframe);
        IKeyFrame? prev = null;
        IKeyFrame? next = null;

        if (index >= 0)
        {
            int prevIndex = index - 1;
            int nextIndex = index + 1;
            if (0 <= prevIndex && prevIndex < Count)
            {
                prev = this[prevIndex];
            }
            if (0 <= nextIndex && nextIndex < Count)
            {
                next = this[nextIndex];
            }
        }

        return (prev, next);
    }

    public (IKeyFrame? Previous, IKeyFrame? Next) GetPreviousAndNextKeyFrame(TimeSpan timeSpan)
    {
        int index = IndexAtOrCount(timeSpan);
        IKeyFrame? prev = index > 0 ? this[index - 1] : null;
        IKeyFrame? next = index < Count ? this[index] : null;
        return (prev, next);
    }

    public int IndexAtOrCount(TimeSpan timeSpan)
    {
        for (int i = 0; i < Count; i++)
        {
            IKeyFrame next = this[i];
            if (timeSpan <= next.KeyTime)
            {
                return i;
            }
        }

        return Count;
    }
}
